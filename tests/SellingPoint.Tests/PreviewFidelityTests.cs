using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// The panel in Definições and the bytes sent to the printer are laid out by the
/// same code on purpose, so what is seen on a Mac with no printer attached is what
/// comes out of the paper. These are the tests that keep that true.
/// </summary>
public class PreviewFidelityTests
{
    private static readonly SlipStyle[] Styles =
    [
        SlipStyle.None,
        SlipStyle.Bold,
        SlipStyle.DoubleHeight,
        SlipStyle.DoubleWidth,
        SlipStyle.DoubleWidth | SlipStyle.DoubleHeight,
        SlipStyle.Bold | SlipStyle.DoubleWidth | SlipStyle.DoubleHeight
    ];

    [Fact]
    public void The_preview_draws_a_line_at_the_width_the_printer_will_give_it()
    {
        // The complaint this exists for: the panel does not look like the paper.
        //
        // The encoder combines the base size and the line's own emphasis by taking
        // the larger, never by multiplying - that is what stops a senha at the
        // large size coming out in quadruple letters. At a base size that is
        // already doubled, a DoubleWidth line therefore prints no wider than
        // anything else, and has the full width of the paper to use.
        //
        // The preview halved the columns whenever it saw DoubleWidth, whatever the
        // base size. At the large size it drew the product name in 12 columns
        // where the printer uses 24: half the width, and centred in the wrong
        // place.
        foreach (var paper in Enum.GetValues<PaperWidth>())
        foreach (var font in Enum.GetValues<TicketFontSize>())
        foreach (var style in Styles)
        {
            var options = new TicketOptions { Paper = paper, FontSize = font };
            var line = new SlipTextLine(new string('X', 200), SlipAlign.Left, style);

            var drawn = SlipPreview.ToText([line], options).TrimEnd('\r', '\n').Length;

            Assert.Equal(PaperFormat.UsableColumns(paper, font, style), drawn);
        }
    }

    [Fact]
    public void The_width_the_preview_assumes_is_the_one_the_printer_is_told()
    {
        // The other half of the agreement: the number the preview divides by is
        // the number that actually goes down the wire in GS !.
        foreach (var paper in Enum.GetValues<PaperWidth>())
        foreach (var font in Enum.GetValues<TicketFontSize>())
        foreach (var style in Styles)
        {
            var options = new TicketOptions { Paper = paper, FontSize = font };
            var bytes = EscPosEncoder.Encode([new SlipTextLine("Cerveja", SlipAlign.Left, style)], options);

            // GS ! packs the width multiplier into the high nibble, minus one.
            var sent = (SizeBytes(bytes).First() >> 4) + 1;

            Assert.Equal(PaperFormat.EffectiveWidth(style, font), sent);
        }
    }

    [Fact]
    public void A_line_twice_as_tall_costs_twice_the_paper_whichever_made_it_tall()
    {
        // The paper estimate counted a line as double height only when the line's
        // own style said so, ignoring that at a doubled base size every line is
        // already twice as tall. The millimetres shown in Definições were half of
        // what the roll actually gave up.
        var normal = new TicketOptions { Paper = PaperWidth.Wide, FontSize = TicketFontSize.Normal };
        var large = normal with { FontSize = TicketFontSize.Large };

        Assert.True(PaperEstimate.ForGroupSlip(large).Millimetres
                    > PaperEstimate.ForGroupSlip(normal).Millimetres,
            "bigger letters must cost more paper, not the same");
    }

    private static IEnumerable<byte> SizeBytes(byte[] data)
    {
        for (var i = 0; i + 2 < data.Length; i++)
            if (data[i] == 0x1D && data[i + 1] == (byte)'!') yield return data[i + 2];
    }
}
