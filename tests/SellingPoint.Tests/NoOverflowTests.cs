using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// The letter size can be changed, and nothing may ever exceed the paper.
///
/// This is the guarantee, and it is checked rather than promised: every paper
/// width, every letter size, every combination of the eight ticket switches, with
/// product names chosen to be far too long. Not one line may come out wider than
/// the columns available.
/// </summary>
public class NoOverflowTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 22, 31, 0);

    private static readonly string[] AwkwardNames =
    [
        "Sandes de Leitão com Molho da Casa, Batata Frita e Salada",
        "Cerveja",
        "Água das Pedras 33cl",
        "X",
        "Menu Completo Para Duas Pessoas Com Sobremesa e Café Incluídos"
    ];

    /// <summary>Every ticket option that changes what is drawn, on and off.</summary>
    private static IEnumerable<TicketOptions> AllCombinations()
    {
        foreach (var paper in new[] { PaperWidth.Wide, PaperWidth.Narrow })
        foreach (var font in PaperFormat.InSizeOrder)
        foreach (var rules in new[] { true, false })
        foreach (var date in new[] { true, false })
        foreach (var total in new[] { true, false })
        foreach (var prices in new[] { true, false })
        foreach (var header in new[] { "", "FESTA DA ALDEIA DE SÃO MARTINHO 2026" })
        {
            yield return new TicketOptions
            {
                Paper = paper,
                FontSize = font,
                ShowRules = rules,
                ShowDate = date,
                ShowTotalOnGroupSlip = total,
                ShowPricesOnGroupSlip = prices,
                Header = header,
                Footer = header.Length > 0 ? "Obrigado pela sua preferência!" : ""
            };
        }
    }

    private static Sale AwkwardSale() => new()
    {
        TicketNumber = 9999,
        CreatedAt = Now,
        TotalCents = 123456,
        Lines = AwkwardNames.Select((name, i) => new SaleLine
        {
            ProductName = name,
            Qty = i * 7 + 1,
            UnitPriceCents = 12345,
            LineTotalCents = 12345 * (i * 7 + 1),
            PrintGroup = i % 2 == 0 ? "Cozinha" : "Bar",
            SlipMode = i % 2 == 0 ? SlipMode.Grouped : SlipMode.PerUnit,
            CategoryName = "Categoria com um nome comprido"
        }).ToList()
    };

    [Fact]
    public void No_line_of_any_ticket_ever_exceeds_the_paper()
    {
        var sale = AwkwardSale();
        var checkedCombinations = 0;

        foreach (var options in AllCombinations())
        {
            foreach (var slip in TicketBuilder.Build(sale, options with { PrintSummarySlip = true }))
            {
                foreach (var line in SlipPreview.ToText(slip, options).Split(Environment.NewLine))
                {
                    Assert.True(line.Length <= options.Columns,
                        $"{PaperFormat.Describe(options.Paper, options.FontSize)}: "
                        + $"'{line}' tem {line.Length} de {options.Columns} colunas");
                }
            }

            checkedCombinations++;
        }

        // 2 papers x 5 sizes x 2^4 switches x 2 headers.
        Assert.Equal(320, checkedCombinations);
    }

    [Theory]
    [InlineData(PaperWidth.Wide, TicketFontSize.Small, 64)]
    [InlineData(PaperWidth.Wide, TicketFontSize.Normal, 48)]
    [InlineData(PaperWidth.Wide, TicketFontSize.Medium, 32)]
    [InlineData(PaperWidth.Wide, TicketFontSize.Large, 24)]
    [InlineData(PaperWidth.Wide, TicketFontSize.Huge, 16)]
    [InlineData(PaperWidth.Narrow, TicketFontSize.Small, 42)]
    [InlineData(PaperWidth.Narrow, TicketFontSize.Normal, 32)]
    [InlineData(PaperWidth.Narrow, TicketFontSize.Medium, 21)]
    [InlineData(PaperWidth.Narrow, TicketFontSize.Large, 16)]
    [InlineData(PaperWidth.Narrow, TicketFontSize.Huge, 10)]
    public void Bigger_letters_mean_fewer_of_them(PaperWidth paper, TicketFontSize font, int columns)
        => Assert.Equal(columns, PaperFormat.Columns(paper, font));

    [Fact]
    public void The_column_count_follows_the_letter_size_with_no_way_to_disagree()
    {
        // The old settings screen let a column count be typed alongside a font.
        // Now there is one source for the number, so they cannot drift apart.
        var large = new TicketOptions { Paper = PaperWidth.Wide, FontSize = TicketFontSize.Large };

        Assert.Equal(24, large.Columns);
        Assert.Equal(48, (large with { FontSize = TicketFontSize.Normal }).Columns);
    }

    [Fact]
    public void At_the_large_size_a_senha_does_not_double_again_into_quadruple_letters()
    {
        // The senha doubles its product name for legibility. At a base size that is
        // already doubled, multiplying would give four-times-wide letters and a line
        // four times too long for the paper.
        var options = new TicketOptions { Paper = PaperWidth.Wide, FontSize = TicketFontSize.Large };
        var senha = new SenhaSlip("Bar", "#0042-1", Now, "Cerveja", 150);

        var bytes = EscPosEncoder.Encode(SlipRenderer.Render(senha, options), options);

        // GS ! packs width into the high nibble: 0x11 is double, 0x33 quadruple.
        Assert.DoesNotContain(SizeBytes(bytes), size => size > 0x11);
    }

    [Fact]
    public void The_letter_size_reaches_the_printer_as_a_font_command()
    {
        var small = EscPosEncoder.Encode([new SlipTextLine("Cerveja")],
            new TicketOptions { FontSize = TicketFontSize.Small });
        var normal = EscPosEncoder.Encode([new SlipTextLine("Cerveja")],
            new TicketOptions { FontSize = TicketFontSize.Normal });

        Assert.True(Contains(small, 0x1B, (byte)'M', 1));    // font B
        Assert.True(Contains(normal, 0x1B, (byte)'M', 0));   // font A
    }

    [Fact]
    public void A_long_name_loses_its_end_rather_than_the_price()
    {
        // Truncating is how nothing overflows; the price must survive it.
        var options = new TicketOptions { Paper = PaperWidth.Narrow, FontSize = TicketFontSize.Large };
        var slip = new GroupedSlip("Bar", "#0042", Now,
            [new SlipItem(1, "Sandes de Leitão com Molho", 400)], 400);

        // The item line, not the total, which carries the same amount.
        var line = Assert.Single(
            SlipPreview.ToText(slip, options).Split(Environment.NewLine),
            l => l.StartsWith("1x"));

        Assert.True(line.Length <= 16);
        Assert.EndsWith("4,00 €", line);
    }

    private static IEnumerable<byte> SizeBytes(byte[] data)
    {
        for (var i = 0; i + 2 < data.Length; i++)
            if (data[i] == 0x1D && data[i + 1] == (byte)'!') yield return data[i + 2];
    }

    private static bool Contains(byte[] haystack, params byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle)) return true;

        return false;
    }
}
