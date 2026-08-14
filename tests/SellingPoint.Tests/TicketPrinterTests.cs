using SellingPoint.Printing;

namespace SellingPoint.Tests;

public class TicketPrinterTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private sealed class RecordingTransport : IPrintTransport
    {
        public List<(byte[] Data, string Preview)> Sent { get; } = [];

        public void Send(byte[] data, string preview) => Sent.Add((data, preview));
        public string Describe() => "memoria";
    }

    private static Sale MixedSale() => new()
    {
        TicketNumber = 42,
        CreatedAt = Now,
        TotalCents = 750,
        Lines =
        [
            new SaleLine { ProductName = "Cerveja", Qty = 2, UnitPriceCents = 150, LineTotalCents = 300,
                           PrintGroup = "Bar", SlipMode = SlipMode.PerUnit },
            new SaleLine { ProductName = "Bolo", Qty = 1, UnitPriceCents = 150, LineTotalCents = 150,
                           PrintGroup = "Bar", SlipMode = SlipMode.Grouped },
            new SaleLine { ProductName = "Bifana", Qty = 1, UnitPriceCents = 300, LineTotalCents = 300,
                           PrintGroup = "Cozinha", SlipMode = SlipMode.Grouped }
        ]
    };

    [Fact]
    public void Each_slip_is_sent_to_the_transport_once()
    {
        var transport = new RecordingTransport();
        var printer = new TicketPrinter(transport, new TicketOptions());

        // Bar list (Bolo) + 2 beer senhas + Cozinha list = 4.
        Assert.Equal(4, printer.Print(MixedSale()));
        Assert.Equal(4, transport.Sent.Count);
    }

    [Fact]
    public void The_cash_drawer_opens_once_per_sale_not_once_per_slip()
    {
        var transport = new RecordingTransport();
        var printer = new TicketPrinter(transport, new TicketOptions { OpenCashDrawer = true });

        printer.Print(MixedSale());

        var pulses = transport.Sent.Count(s => s.Data.Length >= 5 &&
            Enumerable.Range(0, s.Data.Length - 4).Any(i =>
                s.Data[i] == 0x1B && s.Data[i + 1] == (byte)'p' && s.Data[i + 2] == 0));

        Assert.Equal(1, pulses);
    }

    [Fact]
    public void The_preview_shows_every_slip_the_sale_would_print()
    {
        var printer = new TicketPrinter(new RecordingTransport(), new TicketOptions());

        var preview = printer.Preview(MixedSale());

        Assert.Contains("BAR", preview);
        Assert.Contains("COZINHA", preview);
        Assert.Contains("CERVEJA", preview);   // the senhas
        Assert.Contains("#0042-1", preview);
        Assert.Contains("#0042-2", preview);
        Assert.Contains("1x Bolo", preview);
    }

    [Fact]
    public void The_file_transport_writes_a_readable_slip_next_to_the_raw_bytes()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sellingpoint-print-{Guid.NewGuid():N}");
        try
        {
            var printer = new TicketPrinter(new FileTransport(folder), new TicketOptions());
            printer.Print(MixedSale());

            Assert.Equal(4, Directory.GetFiles(folder, "*.bin").Length);

            var texts = Directory.GetFiles(folder, "*.txt").Select(File.ReadAllText).ToList();
            Assert.Equal(4, texts.Count);
            Assert.Contains(texts, t => t.Contains("1x Bifana"));
            Assert.Contains(texts, t => t.Contains("CERVEJA"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_plain_text_block_can_be_printed_for_the_closing_summary()
    {
        var transport = new RecordingTransport();
        var printer = new TicketPrinter(transport, new TicketOptions());

        printer.PrintText("FECHO DE CAIXA", ["Dinheiro                  180,50 €"]);

        var sent = Assert.Single(transport.Sent);
        Assert.Contains("FECHO DE CAIXA", sent.Preview);
        Assert.Contains("180,50 €", sent.Preview);
    }
}
