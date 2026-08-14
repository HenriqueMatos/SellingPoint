namespace SellingPoint.Tests;

public class TenderTests
{
    [Fact]
    public void Change_is_the_difference()
    {
        Assert.True(Tender.TryChange(450, 500, out var change));
        Assert.Equal(50, change);
    }

    [Fact]
    public void Exact_money_gives_no_change()
    {
        Assert.True(Tender.TryChange(450, 450, out var change));
        Assert.Equal(0, change);
    }

    [Fact]
    public void Too_little_money_fails_and_reports_no_change()
    {
        Assert.False(Tender.TryChange(450, 400, out var change));
        Assert.Equal(0, change);
    }

    [Theory]
    [InlineData(450, new[] { 450, 500, 1000, 2000 })]
    [InlineData(500, new[] { 500, 1000, 2000, 5000 })]
    [InlineData(1250, new[] { 1250, 1500, 2000, 5000 })]
    [InlineData(6000, new[] { 6000 })]
    public void QuickTender_offers_exact_next_round_five_and_likely_notes(int total, int[] expected)
        => Assert.Equal(expected, Tender.QuickTender(total));

    [Fact]
    public void QuickTender_on_an_empty_cart_offers_nothing_useful()
        => Assert.Equal(new[] { 0 }, Tender.QuickTender(0));
}
