using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>
/// Lays a slip out as lines. One layout implementation feeds both the on-screen
/// preview and the printer bytes, so what is previewed on a Mac with no printer
/// attached is what comes out of the printer.
/// </summary>
public static class SlipRenderer
{
    public static IReadOnlyList<SlipTextLine> Render(Slip slip, TicketOptions options) => slip switch
    {
        GroupedSlip grouped => RenderGrouped(grouped, options),
        SenhaSlip senha => RenderSenha(senha, options),
        _ => throw new ArgumentOutOfRangeException(nameof(slip), slip, "Unknown slip type.")
    };

    private static List<SlipTextLine> RenderGrouped(GroupedSlip slip, TicketOptions options)
    {
        var lines = new List<SlipTextLine>();
        var columns = options.Columns;

        AddHeader(lines, options);

        lines.Add(new SlipTextLine(
            Layout.LeftRight(slip.IsSummary ? "" : slip.PrintGroup.ToUpperInvariant(), slip.Reference, columns),
            Style: SlipStyle.Bold));
        lines.Add(new SlipTextLine(slip.CreatedAt.ToString("dd/MM/yyyy HH:mm")));
        lines.Add(new SlipTextLine(Layout.Rule('-', columns)));

        foreach (var item in slip.Items)
        {
            lines.Add(new SlipTextLine(
                Layout.LeftRight($"{item.Qty}x {item.Name}", Money.Format(item.TotalCents), columns)));
        }

        lines.Add(new SlipTextLine(Layout.Rule('-', columns)));
        lines.Add(new SlipTextLine(
            Layout.LeftRight("TOTAL", Money.Format(slip.TotalCents), columns),
            Style: SlipStyle.Bold));

        AddFooter(lines, options);
        return lines;
    }

    private static List<SlipTextLine> RenderSenha(SenhaSlip slip, TicketOptions options)
    {
        var lines = new List<SlipTextLine>();

        AddHeader(lines, options);
        lines.Add(new SlipTextLine(""));

        // Double width halves the usable columns. A name that no longer fits keeps
        // the double height and drops the double width rather than wrapping.
        var name = slip.ItemName.ToUpperInvariant();
        var style = SlipStyle.Bold | SlipStyle.DoubleHeight;
        if (name.Length <= options.Columns / 2) style |= SlipStyle.DoubleWidth;

        lines.Add(new SlipTextLine(name, SlipAlign.Center, style));

        if (options.ShowPriceOnSenha)
            lines.Add(new SlipTextLine(Money.Format(slip.PriceCents), SlipAlign.Center, SlipStyle.DoubleHeight));

        lines.Add(new SlipTextLine(""));
        lines.Add(new SlipTextLine(
            $"{slip.Reference}   {slip.CreatedAt:dd/MM HH:mm}", SlipAlign.Center));

        if (!string.IsNullOrWhiteSpace(slip.PrintGroup))
            lines.Add(new SlipTextLine(slip.PrintGroup.ToUpperInvariant(), SlipAlign.Center));

        lines.Add(new SlipTextLine(Layout.Rule('=', options.Columns)));
        return lines;
    }

    private static void AddHeader(List<SlipTextLine> lines, TicketOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Header))
            lines.Add(new SlipTextLine(options.Header, SlipAlign.Center, SlipStyle.Bold));

        lines.Add(new SlipTextLine(Layout.Rule('=', options.Columns)));
    }

    private static void AddFooter(List<SlipTextLine> lines, TicketOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Footer))
            lines.Add(new SlipTextLine(options.Footer, SlipAlign.Center));
    }
}

public static class Layout
{
    public static string Rule(char character, int columns) => new(character, columns);

    /// <summary>
    /// "2x Cerveja" on the left, "3,00 EUR" hard against the right margin. The left
    /// side is truncated when a long product name would collide with the price -
    /// a wrapped price column is unreadable at a glance in a dark bar.
    /// </summary>
    public static string LeftRight(string left, string right, int columns)
    {
        right ??= "";
        left ??= "";

        var room = columns - right.Length - 1;
        if (room < 1) return Truncate(right, columns);
        if (left.Length > room) left = Truncate(left, room);

        return left.PadRight(columns - right.Length) + right;
    }

    public static string Center(string text, int columns)
    {
        text = Truncate(text, columns);
        var padding = (columns - text.Length) / 2;
        return new string(' ', padding) + text;
    }

    public static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max];
}
