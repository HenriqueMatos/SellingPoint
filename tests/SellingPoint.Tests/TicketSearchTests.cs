using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// Half past midnight, a customer holding slip 87 says the kitchen never served
/// it. Until this existed nobody could see what was on 87 - reprint only ever
/// reached the last sale - so the volunteer either refused someone who was right
/// or handed over food to someone who was not.
/// </summary>
public class TicketSearchTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 21, 0, 0);

    private static AppServices Till(TempDb t, out int sessionId)
    {
        var services = new AppServices(t.Path);
        services.Print.Pause();                 // nothing leaves for paper in a test
        sessionId = services.Sales.OpenSession("Festa", 0, Evening).Id;
        return services;
    }

    private static void Sell(AppServices services, int sessionId, string product, int qty, DateTime at)
    {
        var cart = new Cart();
        cart.Add(services.Catalog.GetProducts().First(p => p.Name == product), qty);

        services.Sales.Save(
            SaleFactory.Build(cart, services.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 100_000, at),
            sessionId);
    }

    [Fact]
    public void A_slip_from_an_hour_ago_can_still_be_looked_at()
    {
        using var t = new TempDb();
        var services = Till(t, out var sessionId);

        Sell(services, sessionId, "Bifana", 2, Evening.AddMinutes(30));
        Sell(services, sessionId, "Cerveja", 1, Evening.AddMinutes(90));

        var sale = services.Sales.GetSaleByTicket(sessionId, 1);

        Assert.NotNull(sale);
        Assert.Equal(1, sale!.TicketNumber);
        Assert.Equal("Bifana", Assert.Single(sale.Lines).ProductName);
        Assert.Equal(600, sale.TotalCents);
    }

    [Fact]
    public void A_number_from_another_night_is_not_this_night()
    {
        // Ticket numbers restart at 1 with every session, so a number on its own
        // does not name a sale. Looking outside the open session would show the
        // customer somebody else's order.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var friday = services.Sales.OpenSession("Sexta", 0, Evening).Id;
        Sell(services, friday, "Bifana", 1, Evening.AddMinutes(30));
        services.Sales.CloseSession(friday, 0, Evening.AddHours(5));

        var saturday = services.Sales.OpenSession("Sábado", 0, Evening.AddDays(1)).Id;
        Sell(services, saturday, "Cerveja", 3, Evening.AddDays(1).AddMinutes(30));

        Assert.Equal("Cerveja", services.Sales.GetSaleByTicket(saturday, 1)!.Lines.Single().ProductName);
        Assert.Equal("Bifana", services.Sales.GetSaleByTicket(friday, 1)!.Lines.Single().ProductName);
    }

    [Fact]
    public void The_screen_shows_what_was_on_the_slip()
    {
        using var t = new TempDb();
        var services = Till(t, out var sessionId);
        Sell(services, sessionId, "Bifana", 2, Evening.AddMinutes(30));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenTicketSearchCommand.Execute(null);
        venda.TicketSearchDigitCommand.Execute("1");
        venda.FindTicketCommand.Execute(null);

        Assert.True(venda.HasFoundTicket);
        Assert.Contains("#0001", venda.TicketSearchResult);
        Assert.Contains("2x Bifana", venda.TicketSearchResult);
        Assert.Contains("6,00 €", venda.TicketSearchResult);
        Assert.Contains("dinheiro", venda.TicketSearchResult);
    }

    [Fact]
    public void A_number_that_is_not_there_says_so_and_offers_no_reprint()
    {
        using var t = new TempDb();
        var services = Till(t, out _);

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenTicketSearchCommand.Execute(null);
        venda.TicketSearchDigitCommand.Execute("9");
        venda.TicketSearchDigitCommand.Execute("9");
        venda.FindTicketCommand.Execute(null);

        Assert.False(venda.HasFoundTicket);
        Assert.Contains("#0099", venda.TicketSearchResult);

        // The reprint must do nothing rather than reprinting whatever was found last.
        venda.ReprintFoundCommand.Execute(null);
        Assert.Equal(0, services.Print.PendingCount);
    }

    [Fact]
    public void Finding_a_slip_and_reprinting_it_queues_its_own_senhas()
    {
        using var t = new TempDb();
        var services = Till(t, out var sessionId);

        Sell(services, sessionId, "Bifana", 1, Evening.AddMinutes(30));
        Sell(services, sessionId, "Cerveja", 1, Evening.AddMinutes(60));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenTicketSearchCommand.Execute(null);
        venda.TicketSearchDigitCommand.Execute("1");
        venda.FindTicketCommand.Execute(null);
        venda.ReprintFoundCommand.Execute(null);

        Assert.False(venda.IsTicketSearchOpen);
        Assert.Contains("#0001", venda.StatusMessage);

        // The first sale's slip, not the second's.
        var queued = services.Print.Pending().Single();
        Assert.Contains("Bifana", queued.Preview);
        Assert.DoesNotContain("Cerveja", queued.Preview);
    }

    [Fact]
    public void Opening_the_panel_forgets_the_last_search()
    {
        // Otherwise the reprint button is live with somebody else's order behind it
        // the moment the panel opens.
        using var t = new TempDb();
        var services = Till(t, out var sessionId);
        Sell(services, sessionId, "Bifana", 1, Evening.AddMinutes(30));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenTicketSearchCommand.Execute(null);
        venda.TicketSearchDigitCommand.Execute("1");
        venda.FindTicketCommand.Execute(null);
        Assert.True(venda.HasFoundTicket);

        venda.CloseTicketSearchCommand.Execute(null);
        venda.OpenTicketSearchCommand.Execute(null);

        Assert.False(venda.HasFoundTicket);
        Assert.Equal("", venda.TicketSearchEntry);
        Assert.Equal("", venda.TicketSearchResult);
    }
}
