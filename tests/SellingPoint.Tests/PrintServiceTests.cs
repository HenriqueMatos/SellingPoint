using SellingPoint.App;
using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// The behaviour that matters when a printer dies mid-event: nothing is lost, and
/// it comes out by itself once the printer is back.
/// </summary>
public class PrintServiceTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 22, 0, 0);

    /// <summary>A printer that can be unplugged and plugged back in from a test.</summary>
    private sealed class FlakyTransport : IPrintTransport, IStatusQueryable
    {
        private const byte Ready = 0x12;

        public bool IsConnected { get; set; } = true;
        public bool IsOutOfPaper { get; set; }
        public List<string> Sent { get; } = [];

        public void Send(byte[] data, string preview)
        {
            if (!IsConnected) throw new IOException("A porta não existe");
            if (IsOutOfPaper) throw new IOException("Sem papel");
            Sent.Add(preview);
        }

        public byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs)
        {
            if (!IsConnected) return null;
            var kind = request[2];
            return kind == 4 && IsOutOfPaper ? [Ready | 0x60] : [Ready];
        }

        public string Describe() => "impressora de teste";
    }

    /// <summary>
    /// Polls rather than sleeps, so a passing test costs milliseconds. The budget is
    /// deliberately generous: the print worker runs on a background thread, and a
    /// machine busy doing something else at the same time should not turn a correct
    /// implementation into a red build.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }

        Assert.Fail($"Timed out waiting for: {what}");
    }

    private static PrintService Build(TempDb t, FlakyTransport transport)
        => new(t.PrintQueue, t.Settings, new TicketPrinter(transport, new TicketOptions()));

    [Fact]
    public async Task A_slip_queued_while_the_printer_is_down_comes_out_when_it_is_back()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport { IsConnected = false };
        using var service = Build(t, transport);
        service.Start();

        service.EnqueueText("FECHO DE CAIXA", ["Dinheiro   180,50 €"]);

        await WaitUntil(() => service.PendingCount == 1 && service.LastError is not null,
            "the failure to be recorded");
        Assert.Empty(transport.Sent);

        // Someone plugs the cable back in.
        transport.IsConnected = true;
        service.RetryNow();

        await WaitUntil(() => service.PendingCount == 0, "the queue to drain");
        Assert.Single(transport.Sent);
        Assert.Contains("180,50 €", transport.Sent[0]);
    }

    [Fact]
    public async Task Every_slip_of_a_sale_is_queued_and_drains_in_order()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport { IsConnected = false };
        using var service = Build(t, transport);
        service.Start();

        var bebidas = t.Catalog.GetCategories().Single(c => c.Name == "Bebidas");
        bebidas.SlipMode = SlipMode.PerUnit;
        t.Catalog.UpdateCategory(bebidas);

        var session = t.Sales.OpenSession("Festa", 0, Evening);
        var products = t.Catalog.GetProducts();
        var cart = new Cart();
        cart.Add(products.First(p => p.Name == "Cerveja"), 2);
        cart.Add(products.First(p => p.Name == "Bifana"));

        var sale = t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 1000, Evening), session.Id);

        // Two beer senhas plus the kitchen list.
        Assert.Equal(3, service.Enqueue(sale));
        await WaitUntil(() => service.PendingCount == 3, "all three slips to be queued");

        transport.IsConnected = true;
        service.RetryNow();

        await WaitUntil(() => service.PendingCount == 0, "the queue to drain");
        Assert.Equal(3, transport.Sent.Count);

        // Oldest first: the senhas were composed before the kitchen list.
        Assert.Contains("CERVEJA", transport.Sent[0]);
        Assert.Contains("1x Bifana", transport.Sent[2]);
    }

    [Fact]
    public async Task Running_out_of_paper_is_reported_as_something_fixable()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport { IsOutOfPaper = true };
        using var service = Build(t, transport);
        service.Start();

        service.EnqueueText("TESTE", ["linha"]);

        await WaitUntil(() => service.Status.State == PrinterState.PaperOut, "the paper-out status");
        Assert.True(service.Status.NeedsAttention);
        Assert.Equal(1, service.PendingCount);

        transport.IsOutOfPaper = false;
        service.RetryNow();

        await WaitUntil(() => service.PendingCount == 0, "the queue to drain after a new roll");
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task A_paused_queue_holds_everything_until_it_is_resumed()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport();
        using var service = Build(t, transport);
        service.Start();
        service.Pause();

        service.EnqueueText("TESTE", ["linha"]);

        await Task.Delay(200);
        Assert.Empty(transport.Sent);
        Assert.Equal(1, service.PendingCount);

        service.Resume();
        await WaitUntil(() => service.PendingCount == 0, "the queue to drain after resuming");
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task A_queue_survives_the_app_being_closed_and_reopened()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport { IsConnected = false };

        using (var first = Build(t, transport))
        {
            first.Start();
            first.EnqueueText("TESTE", ["linha que tem de sair"]);
            await WaitUntil(() => first.PendingCount == 1, "the slip to be queued");
        }

        // A new session, a new service, the same database.
        transport.IsConnected = true;
        using var second = Build(t, transport);
        Assert.Equal(1, second.PendingCount);

        second.Start();
        await WaitUntil(() => second.PendingCount == 0, "the queue to drain on the next run");
        Assert.Contains("linha que tem de sair", transport.Sent[0]);
    }

    [Fact]
    public async Task Discarding_the_queue_throws_away_only_what_has_not_printed()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport();
        using var service = Build(t, transport);
        service.Start();

        service.EnqueueText("PRIMEIRO", ["a"]);
        await WaitUntil(() => service.PendingCount == 0, "the first slip to print");

        service.Pause();
        service.EnqueueText("SEGUNDO", ["b"]);
        await WaitUntil(() => service.PendingCount == 1, "the second slip to queue");

        service.DiscardPending();

        Assert.Equal(0, service.PendingCount);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task Failures_are_recorded_against_the_slip_so_the_operator_can_see_why()
    {
        using var t = new TempDb();
        var transport = new FlakyTransport { IsConnected = false };
        using var service = Build(t, transport);
        service.Start();

        service.EnqueueText("TESTE", ["linha"]);
        await WaitUntil(() => service.Pending().FirstOrDefault()?.Attempts > 0, "an attempt to be recorded");

        var job = Assert.Single(service.Pending());
        Assert.Equal("TESTE", job.Title);

        // Recorded in the language the operator reads, not the framework's.
        Assert.Equal("Falha na ligação à impressora", job.LastError);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "A porta está a ser usada por outro programa")]
    [InlineData(typeof(FileNotFoundException), "A porta já não existe")]
    [InlineData(typeof(TimeoutException), "A impressora não respondeu a tempo")]
    [InlineData(typeof(IOException), "Falha na ligação à impressora")]
    public void Framework_errors_are_reported_in_words_an_operator_can_act_on(Type type, string expected)
        => Assert.Equal(expected, PrinterErrors.Describe((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void Errors_this_project_raises_are_already_in_portuguese_and_are_left_alone()
        => Assert.Equal("Nenhuma porta série configurada.",
            PrinterErrors.Describe(new InvalidOperationException("Nenhuma porta série configurada.")));
}
