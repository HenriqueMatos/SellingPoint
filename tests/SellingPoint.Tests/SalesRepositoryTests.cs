namespace SellingPoint.Tests;

public class SalesRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private static Sale SaleOf(TempDb t, params (string Product, int Qty)[] items)
    {
        var categories = t.Catalog.GetCategories().ToDictionary(c => c.Id);
        var products = t.Catalog.GetProducts();
        var cart = new Cart();

        foreach (var (name, qty) in items)
            cart.Add(products.First(p => p.Name == name), qty);

        return SaleFactory.Build(cart, categories, PaymentMethod.Card, 0, Now);
    }

    [Fact]
    public void Only_one_session_can_be_open_at_a_time()
    {
        using var t = new TempDb();
        t.Sales.OpenSession("Festa de Sabado", 5000, Now);

        var second = Assert.Throws<InvalidOperationException>(
            () => t.Sales.OpenSession("Outra", 0, Now));

        Assert.Contains("Festa de Sabado", second.Message);
    }

    [Fact]
    public void Closing_a_session_frees_the_till_for_the_next_one()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Sexta", 5000, Now);

        t.Sales.CloseSession(session.Id, 18450, Now.AddHours(5));

        Assert.Null(t.Sales.GetOpenSession());
        t.Sales.OpenSession("Sabado", 5000, Now.AddDays(1));
        Assert.Equal("Sabado", t.Sales.GetOpenSession()!.Name);
    }

    [Fact]
    public void Ticket_numbers_start_at_one_and_count_up_within_the_session()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);

        Assert.Equal(1, t.Sales.Save(SaleOf(t, ("Cerveja", 1)), session.Id).TicketNumber);
        Assert.Equal(2, t.Sales.Save(SaleOf(t, ("Bolo", 1)), session.Id).TicketNumber);
        Assert.Equal(3, t.Sales.Save(SaleOf(t, ("Bifana", 1)), session.Id).TicketNumber);
    }

    [Fact]
    public void Ticket_numbers_restart_with_each_session()
    {
        using var t = new TempDb();
        var friday = t.Sales.OpenSession("Sexta", 0, Now);
        t.Sales.Save(SaleOf(t, ("Cerveja", 1)), friday.Id);
        t.Sales.Save(SaleOf(t, ("Cerveja", 1)), friday.Id);
        t.Sales.CloseSession(friday.Id, 0, Now.AddHours(4));

        var saturday = t.Sales.OpenSession("Sabado", 0, Now.AddDays(1));

        Assert.Equal(1, t.Sales.Save(SaleOf(t, ("Cerveja", 1)), saturday.Id).TicketNumber);
    }

    [Fact]
    public void A_saved_sale_reloads_with_all_of_its_lines()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);
        var saved = t.Sales.Save(SaleOf(t, ("Cerveja", 2), ("Bifana", 1)), session.Id);

        var loaded = t.Sales.GetSale(saved.Id)!;

        Assert.Equal(saved.TotalCents, loaded.TotalCents);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.Equal(600, loaded.TotalCents); // 2 x 1,50 + 3,00
        Assert.Equal(PaymentMethod.Card, loaded.PaymentMethod);
    }

    [Fact]
    public void Cash_sales_persist_what_was_handed_over_and_given_back()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);
        var categories = t.Catalog.GetCategories().ToDictionary(c => c.Id);
        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Cerveja"), 3);

        var sale = SaleFactory.Build(cart, categories, PaymentMethod.Cash, 1000, Now);
        var loaded = t.Sales.GetSale(t.Sales.Save(sale, session.Id).Id)!;

        Assert.Equal(450, loaded.TotalCents);
        Assert.Equal(1000, loaded.CashReceivedCents);
        Assert.Equal(550, loaded.ChangeCents);
        Assert.Equal(PaymentMethod.Cash, loaded.PaymentMethod);
    }

    [Fact]
    public void Selling_decrements_stock_only_for_tracked_products()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);

        var beer = t.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.TrackStock = true;
        beer.StockQty = 20;
        t.Catalog.UpdateProduct(beer);

        var bolo = t.Catalog.GetProducts().First(p => p.Name == "Bolo");

        t.Sales.Save(SaleOf(t, ("Cerveja", 3), ("Bolo", 2)), session.Id);

        var after = t.Catalog.GetProducts();
        Assert.Equal(17, after.First(p => p.Id == beer.Id).StockQty);
        Assert.Equal(0, after.First(p => p.Id == bolo.Id).StockQty); // untracked, left alone
    }

    [Fact]
    public void A_price_change_does_not_rewrite_what_an_earlier_sale_charged()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);
        var saved = t.Sales.Save(SaleOf(t, ("Cerveja", 2)), session.Id);

        var beer = t.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.PriceCents = 200;
        beer.Name = "Cerveja Grande";
        t.Catalog.UpdateProduct(beer);

        var loaded = t.Sales.GetSale(saved.Id)!;
        Assert.Equal("Cerveja", loaded.Lines[0].ProductName);
        Assert.Equal(150, loaded.Lines[0].UnitPriceCents);
        Assert.Equal(300, loaded.TotalCents);
    }

    [Fact]
    public void The_last_sale_is_available_for_reprinting()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Now);
        Assert.Null(t.Sales.GetLastSale(session.Id));

        t.Sales.Save(SaleOf(t, ("Cerveja", 1)), session.Id);
        var second = t.Sales.Save(SaleOf(t, ("Sandes de Leitão", 1)), session.Id);

        var last = t.Sales.GetLastSale(session.Id)!;
        Assert.Equal(second.Id, last.Id);

        // Accents survive the round trip through SQLite.
        Assert.Equal("Sandes de Leitão", last.Lines.Single().ProductName);
    }
}
