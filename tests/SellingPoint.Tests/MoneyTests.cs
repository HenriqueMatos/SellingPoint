namespace SellingPoint.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(0, "0,00 €")]
    [InlineData(5, "0,05 €")]
    [InlineData(150, "1,50 €")]
    [InlineData(-150, "-1,50 €")]
    [InlineData(123456, "1.234,56 €")]
    [InlineData(100000, "1.000,00 €")]
    public void Format_uses_portuguese_conventions(int cents, string expected)
        => Assert.Equal(expected, Money.Format(cents));

    [Fact]
    public void FormatPlain_omits_the_currency_sign_for_csv()
        => Assert.Equal("1.234,56", Money.FormatPlain(123456));

    [Theory]
    [InlineData("1,50", 150)]
    [InlineData("1.50", 150)]
    [InlineData("1,5", 150)]
    [InlineData("2", 200)]
    [InlineData("0,05", 5)]
    [InlineData("1,50 €", 150)]
    [InlineData(" 12,00 ", 1200)]
    [InlineData("-1,50", -150)]
    public void TryParseEuros_accepts_what_people_actually_type(string input, int expected)
    {
        Assert.True(Money.TryParseEuros(input, out var cents));
        Assert.Equal(expected, cents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("€")]
    // Ambiguous: is this 1500,00 or 1,50? Refusing beats guessing on a price field.
    [InlineData("1.234,50")]
    public void TryParseEuros_rejects_junk_and_ambiguity(string? input)
        => Assert.False(Money.TryParseEuros(input, out _));

    [Fact]
    public void Cent_arithmetic_is_exact_where_floating_point_would_drift()
    {
        // 0,85 € x 7. In doubles this is 5.949999999999999.
        var total = 85 * 7;
        Assert.Equal(595, total);
        Assert.Equal("5,95 €", Money.Format(total));
    }
}
