namespace SellingPoint.Tests;

public class CartTests
{
    private static Product Beer(int stock = 0, bool track = false) => new()
    {
        Id = 1, Name = "Cerveja", PriceCents = 150, TrackStock = track, StockQty = stock
    };

    private static Product Cake() => new() { Id = 2, Name = "Bolo", PriceCents = 200 };

    [Fact]
    public void Adding_the_same_product_twice_increments_one_line()
    {
        var cart = new Cart();
        var beer = Beer();

        cart.Add(beer);
        cart.Add(beer);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Qty);
        Assert.Equal(300, cart.TotalCents);
        Assert.Equal(2, cart.ItemCount);
    }

    [Fact]
    public void Different_products_get_their_own_lines()
    {
        var cart = new Cart();
        cart.Add(Beer(), 2);
        cart.Add(Cake());

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(500, cart.TotalCents);
        Assert.Equal(3, cart.ItemCount);
    }

    [Fact]
    public void Decrement_removes_the_line_at_zero()
    {
        var cart = new Cart();
        var beer = Beer();
        cart.Add(beer, 2);

        cart.Decrement(beer);
        Assert.Equal(1, Assert.Single(cart.Lines).Qty);

        cart.Decrement(beer);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, cart.TotalCents);
    }

    [Fact]
    public void Decrementing_something_not_in_the_cart_is_a_no_op()
    {
        var cart = new Cart();
        cart.Add(Beer());

        cart.Decrement(Cake());

        Assert.Single(cart.Lines);
    }

    [Fact]
    public void Remove_and_clear_empty_the_cart()
    {
        var cart = new Cart();
        var beer = Beer();
        cart.Add(beer, 5);
        cart.Add(Cake());

        cart.Remove(beer);
        Assert.Single(cart.Lines);

        cart.Clear();
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Line_price_is_snapshotted_so_a_mid_order_price_edit_cannot_move_the_total()
    {
        var cart = new Cart();
        var beer = Beer();
        cart.Add(beer, 2);

        beer.PriceCents = 900;

        Assert.Equal(300, cart.TotalCents);
    }

    [Fact]
    public void Untracked_products_ignore_stock_entirely()
    {
        var cart = new Cart { OutOfStock = OutOfStockBehaviour.Block };

        Assert.Equal(AddResult.Added, cart.Add(Beer(stock: 0, track: false), 99));
    }

    [Fact]
    public void Warn_sells_past_zero_stock_and_flags_it()
    {
        var cart = new Cart { OutOfStock = OutOfStockBehaviour.Warn };
        var beer = Beer(stock: 5, track: true);

        Assert.Equal(AddResult.Added, cart.Add(beer, 3));
        Assert.Equal(AddResult.AddedBeyondStock, cart.Add(beer, 3));
        Assert.Equal(6, Assert.Single(cart.Lines).Qty);
    }

    [Fact]
    public void Block_refuses_and_leaves_the_cart_untouched()
    {
        var cart = new Cart { OutOfStock = OutOfStockBehaviour.Block };
        var beer = Beer(stock: 5, track: true);

        Assert.Equal(AddResult.Added, cart.Add(beer, 3));
        Assert.Equal(AddResult.Blocked, cart.Add(beer, 3));
        Assert.Equal(3, Assert.Single(cart.Lines).Qty);
    }

    [Fact]
    public void Stock_is_checked_against_the_resulting_quantity_not_the_added_one()
    {
        // Six taps on a product with five in stock must not slip through as six
        // separate "is 1 <= 5?" checks.
        var cart = new Cart { OutOfStock = OutOfStockBehaviour.Block };
        var beer = Beer(stock: 5, track: true);

        for (var i = 0; i < 5; i++)
            Assert.Equal(AddResult.Added, cart.Add(beer));

        Assert.Equal(AddResult.Blocked, cart.Add(beer));
        Assert.Equal(5, Assert.Single(cart.Lines).Qty);
    }

    [Fact]
    public void Adding_zero_or_negative_quantity_is_a_programming_error()
    {
        var cart = new Cart();
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.Add(Beer(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.Add(Beer(), -1));
    }
}
