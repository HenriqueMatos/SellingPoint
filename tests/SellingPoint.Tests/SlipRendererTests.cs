using SellingPoint.Printing;

namespace SellingPoint.Tests;

public class SlipRendererTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private static TicketOptions Options(PaperWidth paper = PaperWidth.Wide) => new()
    {
        Paper = paper, Header = "FESTA DA ALDEIA", Footer = "Obrigado!"
    };

    private static GroupedSlip BarSlip() => new(
        "Bar", "#0042", Now,
        [new SlipItem(2, "Cerveja", 300), new SlipItem(1, "Bolo", 150)],
        450);

    private static string[] RenderText(Slip slip, TicketOptions options)
        => SlipPreview.ToText(slip, options).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Nothing_ever_exceeds_the_paper_width()
    {
        foreach (var paper in new[] { PaperWidth.Narrow, PaperWidth.Wide })
        {
            var options = Options(paper);
            var slips = new Slip[]
            {
                BarSlip(),
                new SenhaSlip("Bar", "#0042-1", Now, "Sandes de Leitao com Molho", 400)
            };

            foreach (var line in slips.SelectMany(s => RenderText(s, options)))
                Assert.True(line.Length <= options.Columns, $"'{line}' is {line.Length} of {options.Columns} columns");
        }
    }

    [Fact]
    public void Prices_sit_hard_against_the_right_margin()
    {
        var lines = RenderText(BarSlip(), Options());

        Assert.Contains(lines, l => l.StartsWith("2x Cerveja") && l.EndsWith("3,00 €") && l.Length == 48);
        Assert.Contains(lines, l => l.StartsWith("1x Bolo") && l.EndsWith("1,50 €") && l.Length == 48);
        Assert.Contains(lines, l => l.StartsWith("TOTAL") && l.EndsWith("4,50 €") && l.Length == 48);
    }

    [Fact]
    public void The_group_name_and_ticket_reference_share_the_top_line()
    {
        var line = Assert.Single(RenderText(BarSlip(), Options()), l => l.StartsWith("BAR"));

        Assert.EndsWith("#0042", line);
    }

    [Fact]
    public void The_summary_slip_drops_the_group_name()
    {
        var summary = new GroupedSlip("", "#0042", Now, [new SlipItem(1, "Cerveja", 150)], 150, IsSummary: true);

        var lines = RenderText(summary, Options());

        Assert.DoesNotContain(lines, l => l.Contains("BAR"));
        Assert.Contains(lines, l => l.Trim() == "#0042");
    }

    [Fact]
    public void A_long_product_name_is_truncated_rather_than_pushed_into_the_price()
    {
        var slip = new GroupedSlip("Bar", "#0042", Now,
            [new SlipItem(1, "Sandes de Leitao com Molho da Casa e Batata", 400)], 400);

        var line = Assert.Single(RenderText(slip, Options(PaperWidth.Narrow)), l => l.StartsWith("1x Sandes"));

        Assert.Equal(32, line.Length);
        Assert.EndsWith("4,00 €", line);
    }

    [Fact]
    public void A_senha_shows_the_item_big_and_centred()
    {
        var senha = new SenhaSlip("Bar", "#0042-1", Now, "Cerveja", 150);

        var lines = SlipRenderer.Render(senha, Options());
        var name = Assert.Single(lines, l => l.Text == "CERVEJA");

        Assert.Equal(SlipAlign.Center, name.Align);
        Assert.True(name.Style.HasFlag(SlipStyle.DoubleWidth));
        Assert.True(name.Style.HasFlag(SlipStyle.DoubleHeight));
    }

    [Fact]
    public void A_name_too_wide_to_double_keeps_the_height_and_drops_the_width()
    {
        // 26 characters will not fit in 32/2 = 16 columns of double-width glyphs.
        var senha = new SenhaSlip("Bar", "#0042-1", Now, "Sandes de Leitao com Molho", 400);

        var lines = SlipRenderer.Render(senha, Options(PaperWidth.Narrow));
        var name = Assert.Single(lines, l => l.Text.StartsWith("SANDES"));

        Assert.False(name.Style.HasFlag(SlipStyle.DoubleWidth));
        Assert.True(name.Style.HasFlag(SlipStyle.DoubleHeight));
    }

    [Fact]
    public void The_price_can_be_kept_off_the_senha()
    {
        var senha = new SenhaSlip("Bar", "#0042-1", Now, "Cerveja", 150);

        var withPrice = RenderText(senha, Options() with { ShowPriceOnSenha = true });
        var without = RenderText(senha, Options() with { ShowPriceOnSenha = false });

        Assert.Contains(withPrice, l => l.Contains("1,50 €"));
        Assert.DoesNotContain(without, l => l.Contains("1,50 €"));
    }

    [Fact]
    public void The_header_is_centred_on_the_paper()
    {
        var line = Assert.Single(RenderText(BarSlip(), Options()), l => l.Contains("FESTA DA ALDEIA"));

        // 48 columns, 15 characters => 16 spaces of lead-in.
        Assert.Equal(new string(' ', 16) + "FESTA DA ALDEIA", line);
    }

    [Fact]
    public void An_empty_header_leaves_no_blank_line_at_the_top()
    {
        var lines = RenderText(BarSlip(), Options() with { Header = "" });

        Assert.StartsWith("====", lines[0]);
    }
}
