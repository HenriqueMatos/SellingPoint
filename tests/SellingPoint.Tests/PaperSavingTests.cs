using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// Every line removed here is a line saved on every ticket of every order, all
/// night. These check that each switch removes what it claims to and nothing else.
/// </summary>
public class PaperSavingTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private static TicketOptions Full => new()
    {
        Paper = PaperWidth.Narrow, Header = "FESTA DA ALDEIA", Footer = "Obrigado!"
    };

    private static GroupedSlip BarSlip(bool summary = false) => new(
        summary ? "" : "Bar", "#0042", Now,
        [new SlipItem(2, "Cerveja", 300), new SlipItem(1, "Bolo", 150)],
        450, IsSummary: summary);

    private static SenhaSlip Senha() => new("Bar", "#0042-1", Now, "Cerveja", 150);

    private static string[] Render(Slip slip, TicketOptions options)
        => SlipPreview.ToText(slip, options).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Turning_off_the_rules_removes_the_separator_lines()
    {
        var with = Render(BarSlip(), Full);
        var without = Render(BarSlip(), Full with { ShowRules = false });

        Assert.Contains(with, l => l.StartsWith("===") || l.StartsWith("---"));
        Assert.DoesNotContain(without, l => l.StartsWith("===") || l.StartsWith("---"));
        Assert.True(without.Length < with.Length);
    }

    [Fact]
    public void Turning_off_the_date_line_moves_the_time_onto_the_reference()
    {
        var without = Render(BarSlip(), Full with { ShowDate = false });

        Assert.DoesNotContain(without, l => l.StartsWith("14/08/2026"));

        // Still knowable when it was rung up - just not on a line of its own.
        Assert.Contains(without, l => l.Contains("#0042") && l.Contains("22:31"));
    }

    [Fact]
    public void Turning_off_the_total_removes_it_from_a_group_slip()
    {
        var without = Render(BarSlip(), Full with { ShowTotalOnGroupSlip = false });

        Assert.DoesNotContain(without, l => l.StartsWith("TOTAL"));
        Assert.Contains(without, l => l.Contains("2x Cerveja"));
    }

    [Fact]
    public void Turning_off_prices_leaves_the_items_readable()
    {
        var without = Render(BarSlip(), Full with { ShowPricesOnGroupSlip = false });

        Assert.Contains(without, l => l.Trim() == "2x Cerveja");
        Assert.DoesNotContain(without, l => l.Contains("3,00 €"));
    }

    [Fact]
    public void The_customer_summary_keeps_its_prices_and_total_regardless()
    {
        // The bar handles no money; the summary is the one that has to add up.
        var slip = Render(BarSlip(summary: true),
            Full with { ShowPricesOnGroupSlip = false, ShowTotalOnGroupSlip = false });

        Assert.Contains(slip, l => l.Contains("3,00 €"));
        Assert.Contains(slip, l => l.StartsWith("TOTAL") && l.EndsWith("4,50 €"));
    }

    [Fact]
    public void A_senha_without_rules_keeps_the_group_name_beside_the_reference()
    {
        // The bar still has to know the slip is theirs.
        var without = Render(Senha(), Full with { ShowRules = false });

        Assert.Contains(without, l => l.Contains("#0042-1") && l.Contains("BAR"));
        Assert.Contains(without, l => l.Contains("CERVEJA"));
    }

    [Fact]
    public void Everything_off_is_much_shorter_and_still_says_what_and_how_many()
    {
        var lean = Full with
        {
            Header = "", Footer = "", ShowRules = false, ShowDate = false,
            ShowTotalOnGroupSlip = false, ShowPricesOnGroupSlip = false
        };

        var before = Render(BarSlip(), Full);
        var after = Render(BarSlip(), lean);

        Assert.True(after.Length * 2 < before.Length, $"{before.Length} -> {after.Length} is not half");
        Assert.Contains(after, l => l.Contains("BAR") && l.Contains("#0042"));
        Assert.Contains(after, l => l.Contains("2x Cerveja"));
    }

    [Fact]
    public void Line_spacing_is_sent_only_when_set()
    {
        var normal = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Full);
        var tight = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Full with { LineSpacingDots = 24 });

        Assert.False(Contains(normal, 0x1B, (byte)'3'));
        Assert.True(Contains(tight, 0x1B, (byte)'3', 24));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    [InlineData(0)]
    public void The_feed_before_the_cut_is_what_was_asked_for(int lines)
    {
        var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja")], Full with { FeedLinesBeforeCut = lines });

        Assert.True(Contains(bytes, 0x1B, (byte)'d', (byte)lines));
        Assert.True(Contains(bytes, 0x1D, (byte)'V', 66, 0));   // still cuts
    }

    [Fact]
    public void The_estimate_counts_the_slip_that_is_actually_rendered()
    {
        // A number that drifts from what prints is worse than no number at all.
        var options = Full with { FeedLinesBeforeCut = 4 };

        var rendered = Render(BarSlip(), options).Length;
        var estimate = PaperEstimate.ForGroupSlip(options);

        Assert.Equal(rendered + 4, estimate.Lines);
    }

    [Fact]
    public void A_senha_counts_its_double_height_lines_as_two()
    {
        var options = Full with { FeedLinesBeforeCut = 0, ShowPriceOnSenha = true };

        // Counted including the blank lines, which cost paper like any other.
        var printed = SlipRenderer.Render(Senha(), options).Count;
        var estimate = PaperEstimate.ForSenha(options);

        // The name and the price are both double height, so two lines each.
        Assert.Equal(printed + 2, estimate.Lines);
    }

    [Fact]
    public void Tighter_spacing_shows_up_as_fewer_millimetres()
    {
        var normal = PaperEstimate.ForGroupSlip(Full);
        var tight = PaperEstimate.ForGroupSlip(Full with { LineSpacingDots = 24 });

        Assert.Equal(normal.Lines, tight.Lines);
        Assert.True(tight.Millimetres < normal.Millimetres);
    }

    private static bool Contains(byte[] haystack, params byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle)) return true;

        return false;
    }
}
