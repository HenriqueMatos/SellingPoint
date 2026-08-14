using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>What a ticket costs in paper, for a given set of options.</summary>
public sealed record PaperCost(int Lines, int Millimetres)
{
    public override string ToString() => $"{Lines} linhas, cerca de {Millimetres} mm";
}

/// <summary>
/// Turns ticket options into millimetres of paper. With eight switches to choose
/// from, seeing the effect of a change is what makes the screen understandable -
/// otherwise it is guessing.
///
/// Measured by rendering a real slip and counting it, not by a formula, so the
/// number cannot drift away from what actually prints. A number that lies is
/// worse than no number.
/// </summary>
public static class PaperEstimate
{
    /// <summary>Printer default line height, in dots.</summary>
    private const int DefaultSpacingDots = 30;

    /// <summary>203 dpi is the near-universal thermal resolution: 8 dots to the millimetre.</summary>
    private const double DotsPerMillimetre = 8.0;

    /// <summary>A grouped slip with a couple of items - the everyday case.</summary>
    public static PaperCost ForGroupSlip(TicketOptions options, int items = 2)
    {
        var slip = new GroupedSlip(
            "Bar", "#0042", new DateTime(2026, 8, 14, 22, 31, 0),
            Enumerable.Range(1, items)
                .Select(i => new SlipItem(i, $"Produto {i}", 150 * i))
                .ToList(),
            150 * items * (items + 1) / 2);

        return Measure(SlipRenderer.Render(slip, options), options);
    }

    /// <summary>One senha - the per-unit slip the bar collects.</summary>
    public static PaperCost ForSenha(TicketOptions options)
    {
        var slip = new SenhaSlip("Bar", "#0042-1", new DateTime(2026, 8, 14, 22, 31, 0), "Cerveja", 150);

        return Measure(SlipRenderer.Render(slip, options), options);
    }

    private static PaperCost Measure(IReadOnlyList<SlipTextLine> lines, TicketOptions options)
    {
        // A double-height line occupies two lines of paper.
        var printed = lines.Sum(l => l.Style.HasFlag(SlipStyle.DoubleHeight) ? 2 : 1);
        var total = printed + Math.Max(0, options.FeedLinesBeforeCut);

        var spacing = options.LineSpacingDots is > 0 and <= 255
            ? options.LineSpacingDots
            : DefaultSpacingDots;

        return new PaperCost(total, (int)Math.Round(total * spacing / DotsPerMillimetre));
    }
}
