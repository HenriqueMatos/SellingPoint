using SellingPoint.App;
using SellingPoint.App.ViewModels;
using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// The band's beers and the mayor's coffee. They leave the bar like anything else -
/// slip, stock, units on the report - and no money follows them, so the drawer must
/// never be expected to hold their price.
/// </summary>
public class OfferTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 21, 0, 0);

    private sealed class Fixture : IDisposable
    {
        public TempDb T { get; } = new();
        public ReportRepository Reports { get; }
        public Session Session { get; }

        public Fixture(int floatCents = 5000)
        {
            Reports = new ReportRepository(T.Db);
            Session = T.Sales.OpenSession("Festa", floatCents, Evening);
        }

        public void Sell(PaymentMethod method, int cashReceived, params (string, int)[] items)
        {
            var categories = T.Catalog.GetCategories().ToDictionary(c => c.Id);
            var products = T.Catalog.GetProducts();
            var cart = new Cart();

            foreach (var (name, qty) in items)
                cart.Add(products.First(p => p.Name == name), qty);

            T.Sales.Save(SaleFactory.Build(cart, categories, method, cashReceived, Evening), Session.Id);
        }

        public SessionReport Report() => Reports.Build(T.Sales.GetSessions().First(s => s.Id == Session.Id));

        public void Dispose() => T.Dispose();
    }

    // --- the sale itself ----------------------------------------------------

    [Fact]
    public void An_offer_keeps_its_price_and_asks_for_nothing()
    {
        // The price is what makes the offer worth reporting: "we gave away 42 EUR"
        // is the sentence somebody wants in the morning.
        var products = new[] { new Product { Id = 1, CategoryId = 1, Name = "Cerveja", PriceCents = 150 } };
        var cart = new Cart();
        cart.Add(products[0], 3);

        var sale = SaleFactory.Build(cart, new Dictionary<int, Category>(), PaymentMethod.Offer, 0, Evening);

        Assert.Equal(PaymentMethod.Offer, sale.PaymentMethod);
        Assert.Equal(450, sale.TotalCents);
        Assert.Equal(450, Assert.Single(sale.Lines).LineTotalCents);
        Assert.Equal(0, sale.CashReceivedCents);
        Assert.Equal(0, sale.ChangeCents);
    }

    [Fact]
    public void An_offer_never_asks_for_money_it_was_not_given()
    {
        // Cash refuses to complete when what was handed over is short of the total.
        // An offer is short of the total by the whole of it, so it must not go down
        // that path at all.
        var cart = new Cart();
        cart.Add(new Product { Id = 1, CategoryId = 1, Name = "Cerveja", PriceCents = 150 }, 1);

        var sale = SaleFactory.Build(cart, new Dictionary<int, Category>(), PaymentMethod.Offer, 0, Evening);

        Assert.Equal(150, sale.TotalCents);
    }

    // --- what the night's report says ---------------------------------------

    [Fact]
    public void Offers_are_summed_apart_from_the_money_that_came_in()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 1000, ("Cerveja", 2));    // 3,00
        f.Sell(PaymentMethod.Card, 0, ("Bifana", 1));        // 3,00
        f.Sell(PaymentMethod.Offer, 0, ("Bolo", 2));         // 3,00 given away

        var report = f.Report();

        Assert.Equal(300, report.CashCents);
        Assert.Equal(300, report.CardCents);
        Assert.Equal(300, report.OfferCents);

        // Takings, not turnover: the offer is in none of it.
        Assert.Equal(600, report.TotalCents);
    }

    [Fact]
    public void An_offer_is_not_expected_in_the_drawer()
    {
        // The one thing that must not happen: a night that gave away 30 EUR
        // reporting itself 30 EUR short at the count.
        using var f = new Fixture(floatCents: 5000);
        f.Sell(PaymentMethod.Cash, 300, ("Cerveja", 2));     // 3,00 into the box
        f.Sell(PaymentMethod.Offer, 0, ("Bifana", 10));      // 30,00 out of the bar

        var report = f.Report();

        Assert.Equal(5300, report.ExpectedCashCents);

        f.T.Sales.CloseSession(f.Session.Id, 5300, Evening.AddHours(4));
        Assert.Equal(0, f.Report().VarianceCents);
    }

    [Fact]
    public void An_offer_is_still_a_sale_everywhere_else()
    {
        using var f = new Fixture();
        var beer = f.T.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.TrackStock = true;
        beer.StockQty = 20;
        f.T.Catalog.UpdateProduct(beer);

        f.Sell(PaymentMethod.Cash, 1000, ("Cerveja", 2));
        f.Sell(PaymentMethod.Offer, 0, ("Cerveja", 3));

        var report = f.Report();

        Assert.Equal(2, report.SalesCount);

        // Units and stock know nothing about who paid - five beers left the bar.
        var line = Assert.Single(report.Stock);
        Assert.Equal(5, line.Sold);
        Assert.Equal(15, line.Remaining);
        Assert.Equal(5, report.Products.Single(p => p.Name == "Cerveja").Units);
        Assert.Equal(5, report.Categories.Single(c => c.Name == "Bebidas").Units);
    }

    [Fact]
    public void A_night_with_no_offers_reports_none()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 1000, ("Cerveja", 2));

        Assert.Equal(0, f.Report().OfferCents);
        Assert.DoesNotContain("Ofertas", ReportRepository.ToCsv(f.Report()));
    }

    [Fact]
    public void The_export_carries_what_was_given_away()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Offer, 0, ("Bolo", 2));

        Assert.Contains("Ofertas;3,00", ReportRepository.ToCsv(f.Report()));
    }

    [Fact]
    public void A_festivals_offers_add_up_across_its_nights()
    {
        using var t = new TempDb();
        var reports = new ReportRepository(t.Db);
        var festival = t.Sales.OpenEvent("Festa da Aldeia", Evening);

        foreach (var night in new[] { 0, 1 })
        {
            var session = t.Sales.OpenSession($"Noite {night + 1}", 0, Evening.AddDays(night), festival.Id);
            var cart = new Cart();
            cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Bolo"), 2);   // 3,00

            t.Sales.Save(
                SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                    PaymentMethod.Offer, 0, Evening.AddDays(night)),
                session.Id);

            t.Sales.CloseSession(session.Id, 0, Evening.AddDays(night).AddHours(5));
        }

        var report = reports.BuildForEvent(festival, t.Sales.GetSessions(festival.Id));

        Assert.Equal(600, report.OfferCents);
        Assert.Equal(0, report.TotalCents);
        Assert.Contains("Ofertas;6,00", ReportRepository.ToCsv(report));
    }

    // --- what comes out of the printer --------------------------------------

    private static Sale OfferSale(SlipMode mode, PaymentMethod method = PaymentMethod.Offer) => new()
    {
        TicketNumber = 42,
        CreatedAt = Evening,
        TotalCents = 300,
        PaymentMethod = method,
        Lines =
        [
            new SaleLine
            {
                ProductName = "Cerveja", Qty = 2, UnitPriceCents = 150, LineTotalCents = 300,
                PrintGroup = "Bar", SlipMode = mode, CategoryName = "Bebidas"
            }
        ]
    };

    [Fact]
    public void Every_slip_of_an_offer_says_it_is_one()
    {
        var options = new TicketOptions { PrintSummarySlip = true };
        var sale = OfferSale(SlipMode.PerUnit);

        var slips = TicketBuilder.Build(sale, options);

        Assert.Equal(3, slips.Count);       // two senhas and the summary
        Assert.All(slips, s => Assert.True(s switch
        {
            GroupedSlip g => g.IsOffer,
            SenhaSlip senha => senha.IsOffer,
            _ => false
        }));

        foreach (var slip in slips)
        {
            var lines = SlipPreview.ToText(slip, options).Split(Environment.NewLine);
            Assert.Contains(lines, l => l.Trim() == "OFERTA");
        }
    }

    [Fact]
    public void A_paid_sale_says_nothing_about_offers()
    {
        var options = new TicketOptions { PrintSummarySlip = true };
        var sale = OfferSale(SlipMode.PerUnit, PaymentMethod.Cash);

        foreach (var slip in TicketBuilder.Build(sale, options))
        {
            var text = SlipPreview.ToText(slip, options);
            Assert.DoesNotContain("OFERTA", text);
        }
    }

    [Fact]
    public void A_senha_for_an_offer_carries_no_price()
    {
        // The senha is what the bar collects. A price on it beside the word OFERTA
        // is the one thing that could still be read as money owed.
        var options = new TicketOptions { ShowPriceOnSenha = true };
        var senha = new SenhaSlip("Bar", "#0042-1", Evening, "Cerveja", 150, IsOffer: true);

        var lines = SlipPreview.ToText(senha, options).Split(Environment.NewLine);

        Assert.Contains(lines, l => l.Trim() == "OFERTA");
        Assert.DoesNotContain(lines, l => l.Contains("1,50"));
    }

    [Fact]
    public void A_group_slip_with_its_total_hidden_still_says_oferta()
    {
        // Hiding the total is a paper setting. It must not take the word with it.
        var options = new TicketOptions { ShowTotalOnGroupSlip = false, ShowPricesOnGroupSlip = false };
        var slip = new GroupedSlip("Bar", "#0042", Evening,
            [new SlipItem(2, "Cerveja", 300)], 300, IsOffer: true);

        var lines = SlipPreview.ToText(slip, options).Split(Environment.NewLine);

        Assert.Contains(lines, l => l.Trim() == "OFERTA");
        Assert.DoesNotContain(lines, l => l.Contains("TOTAL"));
    }

    // --- the till ------------------------------------------------------------

    [Fact]
    public void One_tap_on_oferta_does_not_give_anything_away()
    {
        // Same guard as Cartão, and it matters more here: this is the button whose
        // mis-tap loses the money instead of collecting it.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var sessionId = services.Sales.OpenSession("Festa", 0, Evening).Id;
        var venda = Till(services);

        venda.OpenOfferPanelCommand.Execute(null);

        Assert.True(venda.IsOfferPanelOpen);
        Assert.Null(t.Sales.GetLastSale(sessionId));

        venda.ConfirmOfferCommand.Execute(null);

        Assert.False(venda.IsOfferPanelOpen);
        Assert.True(venda.IsCartEmpty);

        var sale = t.Sales.GetLastSale(sessionId)!;
        Assert.Equal(PaymentMethod.Offer, sale.PaymentMethod);
        Assert.Equal(0, sale.CashReceivedCents);
        Assert.True(sale.TotalCents > 0);
        Assert.True(services.Print.PendingCount > 0);       // paper still comes out
    }

    [Fact]
    public void Cancelling_the_offer_panel_leaves_the_cart_alone()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var sessionId = services.Sales.OpenSession("Festa", 0, Evening).Id;
        var venda = Till(services);
        var total = venda.TotalText;

        venda.OpenOfferPanelCommand.Execute(null);
        venda.CloseOfferPanelCommand.Execute(null);

        Assert.False(venda.IsOfferPanelOpen);
        Assert.Null(t.Sales.GetLastSale(sessionId));
        Assert.False(venda.IsCartEmpty);
        Assert.Equal(total, venda.TotalText);
    }

    [Fact]
    public void The_offer_panel_does_not_open_on_an_empty_cart()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        services.Sales.OpenSession("Festa", 0, Evening);

        var venda = new VendaViewModel(services);
        venda.Load();

        venda.OpenOfferPanelCommand.Execute(null);

        Assert.False(venda.IsOfferPanelOpen);
    }

    [Fact]
    public void A_looked_up_offer_says_it_was_an_offer()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        services.Sales.OpenSession("Festa", 0, Evening);

        var venda = Till(services);
        venda.ConfirmOfferCommand.Execute(null);

        venda.OpenTicketSearchCommand.Execute(null);
        venda.TicketSearchDigitCommand.Execute("1");
        venda.FindTicketCommand.Execute(null);

        Assert.True(venda.HasFoundTicket);
        Assert.Contains("oferta", venda.TicketSearchResult);
    }

    /// <summary>A till with one product in the cart, ready to be given away.</summary>
    private static VendaViewModel Till(AppServices services)
    {
        var venda = new VendaViewModel(services);
        venda.Load();
        venda.Products.First().PressCommand.Execute(null);
        return venda;
    }
}
