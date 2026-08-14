namespace SellingPoint.Tests;

public class ReportRepositoryTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 0, 0);

    private sealed class Fixture : IDisposable
    {
        public TempDb T { get; } = new();
        public ReportRepository Reports { get; }
        public Session Session { get; }

        public Fixture(int floatCents = 5000)
        {
            Reports = new ReportRepository(T.Db);
            Session = T.Sales.OpenSession("Festa", floatCents, Now);
        }

        public void Sell(SellingPoint.Core.PaymentMethod method, int cashReceived, params (string, int)[] items)
        {
            var categories = T.Catalog.GetCategories().ToDictionary(c => c.Id);
            var products = T.Catalog.GetProducts();
            var cart = new Cart();

            foreach (var (name, qty) in items)
                cart.Add(products.First(p => p.Name == name), qty);

            T.Sales.Save(SaleFactory.Build(cart, categories, method, cashReceived, Now), Session.Id);
        }

        public SessionReport Report() => Reports.Build(T.Sales.GetSessions().First(s => s.Id == Session.Id));

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Revenue_is_split_by_how_it_was_paid()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 1000, ("Cerveja", 2));   // 3,00
        f.Sell(PaymentMethod.Card, 0, ("Bifana", 1));       // 3,00
        f.Sell(PaymentMethod.Cash, 500, ("Bolo", 1));       // 1,50

        var report = f.Report();

        Assert.Equal(3, report.SalesCount);
        Assert.Equal(450, report.CashCents);
        Assert.Equal(300, report.CardCents);
        Assert.Equal(750, report.TotalCents);
    }

    [Fact]
    public void Expected_cash_is_the_float_plus_the_cash_taken()
    {
        using var f = new Fixture(floatCents: 5000);
        f.Sell(PaymentMethod.Cash, 1000, ("Cerveja", 2));
        f.Sell(PaymentMethod.Card, 0, ("Bifana", 1));

        // Card money is not in the box, so it must not be expected there.
        Assert.Equal(5300, f.Report().ExpectedCashCents);
    }

    [Fact]
    public void Counting_less_than_expected_shows_a_negative_variance()
    {
        using var f = new Fixture(floatCents: 5000);
        f.Sell(PaymentMethod.Cash, 300, ("Cerveja", 2));

        f.T.Sales.CloseSession(f.Session.Id, 5250, Now.AddHours(4));
        var report = f.Report();

        Assert.Equal(5300, report.ExpectedCashCents);
        Assert.Equal(-50, report.VarianceCents);
    }

    [Fact]
    public void An_open_session_has_no_variance_to_report()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 300, ("Cerveja", 2));

        Assert.Null(f.Report().VarianceCents);
    }

    [Fact]
    public void Sales_are_totalled_per_product_and_per_category()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 2000, ("Cerveja", 4), ("Bifana", 1));
        f.Sell(PaymentMethod.Cash, 2000, ("Cerveja", 2), ("Bolo", 2));

        var report = f.Report();

        var beer = report.Products.Single(p => p.Name == "Cerveja");
        Assert.Equal(6, beer.Units);
        Assert.Equal(900, beer.TotalCents);
        Assert.Equal("Bebidas", beer.CategoryName);

        // Ordered by value, so the thing that made the most money is at the top.
        Assert.Equal("Cerveja", report.Products[0].Name);

        var drinks = report.Categories.Single(c => c.Name == "Bebidas");
        Assert.Equal(6, drinks.Units);
        Assert.Equal(900, drinks.TotalCents);
        Assert.Equal(3, report.Categories.Count);
    }

    [Fact]
    public void A_renamed_product_still_reports_under_the_name_it_was_sold_as()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 500, ("Cerveja", 2));

        var beer = f.T.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.Name = "Cerveja Mini";
        beer.PriceCents = 200;
        f.T.Catalog.UpdateProduct(beer);
        f.Sell(PaymentMethod.Cash, 500, ("Cerveja Mini", 1));

        var report = f.Report();

        Assert.Equal(300, report.Products.Single(p => p.Name == "Cerveja").TotalCents);
        Assert.Equal(200, report.Products.Single(p => p.Name == "Cerveja Mini").TotalCents);
    }

    [Fact]
    public void Stock_shows_what_was_sold_what_was_restocked_and_what_is_left()
    {
        using var f = new Fixture();
        var beer = f.T.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.TrackStock = true;
        beer.StockQty = 20;
        f.T.Catalog.UpdateProduct(beer);

        f.Sell(PaymentMethod.Cash, 2000, ("Cerveja", 6));
        f.T.Catalog.AdjustStock(beer.Id, 12, "Caixa nova", Now, f.Session.Id);

        var line = Assert.Single(f.Report().Stock);   // only tracked products appear
        Assert.Equal("Cerveja", line.Name);
        Assert.Equal(6, line.Sold);
        Assert.Equal(12, line.Adjusted);
        Assert.Equal(26, line.Remaining);             // 20 - 6 + 12
    }

    [Fact]
    public void A_session_with_no_sales_reports_zeroes_rather_than_failing()
    {
        using var f = new Fixture(floatCents: 2000);

        var report = f.Report();

        Assert.Equal(0, report.SalesCount);
        Assert.Equal(0, report.TotalCents);
        Assert.Equal(2000, report.ExpectedCashCents);
        Assert.Empty(report.Products);
    }

    [Fact]
    public void The_csv_uses_semicolons_and_comma_decimals()
    {
        using var f = new Fixture();
        f.Sell(PaymentMethod.Cash, 500, ("Cerveja", 2));

        var csv = ReportRepository.ToCsv(f.Report());

        Assert.Contains("Total;3,00", csv);
        Assert.Contains("Produto;Categoria;Unidades;Total", csv);
        Assert.Contains("Cerveja;Bebidas;2;3,00", csv);
    }

    [Fact]
    public void A_name_containing_a_semicolon_is_quoted_rather_than_splitting_the_row()
    {
        using var f = new Fixture();
        var beer = f.T.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        beer.Name = "Cerveja; grande";
        f.T.Catalog.UpdateProduct(beer);
        f.Sell(PaymentMethod.Cash, 500, ("Cerveja; grande", 2));

        Assert.Contains("\"Cerveja; grande\";Bebidas;2;3,00", ReportRepository.ToCsv(f.Report()));
    }
}
