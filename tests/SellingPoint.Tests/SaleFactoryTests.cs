namespace SellingPoint.Tests;

public class SaleFactoryTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private static readonly Category Bar = new()
    {
        Id = 1, Name = "Bebidas", PrintGroup = "Bar", SlipMode = SlipMode.PerUnit
    };

    private static readonly Product Beer = new()
    {
        Id = 1, CategoryId = 1, Name = "Cerveja", PriceCents = 150
    };

    private static Dictionary<int, Category> Categories => new() { [Bar.Id] = Bar };

    [Fact]
    public void Lines_carry_a_snapshot_of_the_category_print_settings()
    {
        var cart = new Cart();
        cart.Add(Beer, 2);

        var sale = SaleFactory.Build(cart, Categories, PaymentMethod.Card, 0, Now);

        var line = Assert.Single(sale.Lines);
        Assert.Equal("Cerveja", line.ProductName);
        Assert.Equal("Bebidas", line.CategoryName);
        Assert.Equal("Bar", line.PrintGroup);
        Assert.Equal(SlipMode.PerUnit, line.SlipMode);
        Assert.Equal(150, line.UnitPriceCents);
        Assert.Equal(300, line.LineTotalCents);
        Assert.Equal(300, sale.TotalCents);
    }

    [Fact]
    public void Card_sales_record_no_cash_movement()
    {
        var cart = new Cart();
        cart.Add(Beer);

        var sale = SaleFactory.Build(cart, Categories, PaymentMethod.Card, 5000, Now);

        Assert.Equal(0, sale.CashReceivedCents);
        Assert.Equal(0, sale.ChangeCents);
    }

    [Fact]
    public void Cash_sales_record_the_change()
    {
        var cart = new Cart();
        cart.Add(Beer, 3);

        var sale = SaleFactory.Build(cart, Categories, PaymentMethod.Cash, 1000, Now);

        Assert.Equal(450, sale.TotalCents);
        Assert.Equal(550, sale.ChangeCents);
    }

    [Fact]
    public void A_cash_sale_short_of_the_total_is_refused()
    {
        var cart = new Cart();
        cart.Add(Beer, 3);

        Assert.Throws<InvalidOperationException>(
            () => SaleFactory.Build(cart, Categories, PaymentMethod.Cash, 400, Now));
    }

    [Fact]
    public void An_empty_cart_cannot_become_a_sale()
        => Assert.Throws<InvalidOperationException>(
            () => SaleFactory.Build(new Cart(), Categories, PaymentMethod.Card, 0, Now));

    [Fact]
    public void A_product_whose_category_vanished_still_prints_somewhere()
    {
        var cart = new Cart();
        cart.Add(new Product { Id = 9, CategoryId = 404, Name = "Orfao", PriceCents = 100 });

        var line = Assert.Single(SaleFactory.Build(cart, Categories, PaymentMethod.Card, 0, Now).Lines);

        Assert.Equal("Bar", line.PrintGroup);
        Assert.Equal(SlipMode.Grouped, line.SlipMode);
    }
}
