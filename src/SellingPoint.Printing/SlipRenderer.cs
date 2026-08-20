using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>
/// Lays a slip out as lines. One layout implementation feeds both the on-screen
/// preview and the printer bytes, so what is previewed on a Mac with no printer
/// attached is what comes out of the printer.
///
/// Most of what is optional here is optional for one reason: paper. A slip of two
/// items can spend ten of its fourteen lines on decoration.
/// </summary>
public static class SlipRenderer
{
    /// <summary>
    /// What a slip says instead of asking for money. No accents on purpose: it has
    /// to survive a printer whose code page has none.
    /// </summary>
    public const string Offer = "OFERTA";

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

        // With no line of its own for the date, the time rides along with the
        // reference, which is where the eye already is.
        var reference = options.ShowDate
            ? slip.Reference
            : $"{slip.Reference} {slip.CreatedAt:HH:mm}";

        lines.Add(new SlipTextLine(
            Layout.LeftRight(slip.IsSummary ? "" : slip.PrintGroup.ToUpperInvariant(), reference, columns),
            Style: SlipStyle.Bold));

        if (options.ShowDate)
            lines.Add(new SlipTextLine(slip.CreatedAt.ToString("dd/MM/yyyy HH:mm")));

        AddRule(lines, '-', options);

        foreach (var item in slip.Items)
        {
            var label = $"{item.Qty}x {item.Name}";

            // The summary slip is the customer's, so it keeps its prices whatever
            // the group slips are set to - it is the one that has to add up.
            lines.Add(new SlipTextLine(
                options.ShowPricesOnGroupSlip || slip.IsSummary
                    ? Layout.LeftRight(label, Money.Format(item.TotalCents), columns)
                    : Layout.Truncate(label, columns)));
        }

        // Above the total rather than beside it, and outside the block below,
        // because a group slip set to hide its total would otherwise hide the one
        // word that says nobody is paying for this.
        if (slip.IsOffer)
            lines.Add(new SlipTextLine(Offer, SlipAlign.Center, SlipStyle.Bold));

        if (options.ShowTotalOnGroupSlip || slip.IsSummary)
        {
            AddRule(lines, '-', options);
            lines.Add(new SlipTextLine(
                Layout.LeftRight("TOTAL", Money.Format(slip.TotalCents), columns),
                Style: SlipStyle.Bold));
        }

        AddFooter(lines, options);
        return lines;
    }

    private static List<SlipTextLine> RenderSenha(SenhaSlip slip, TicketOptions options)
    {
        var lines = new List<SlipTextLine>();

        AddHeader(lines, options);

        // The blank lines around the name are breathing room, and breathing room
        // is paper. They go with the rules.
        if (options.ShowRules) lines.Add(new SlipTextLine(""));

        // Double width halves the usable columns. A name that no longer fits keeps
        // the double height and drops the double width rather than wrapping.
        var name = slip.ItemName.ToUpperInvariant();
        var style = SlipStyle.Bold | SlipStyle.DoubleHeight;
        if (name.Length <= options.Columns / 2) style |= SlipStyle.DoubleWidth;

        lines.Add(new SlipTextLine(name, SlipAlign.Center, style));

        // In the price's place, and printed even where prices are hidden: it is not
        // a price, it is the reason there is not one.
        if (slip.IsOffer)
            lines.Add(new SlipTextLine(Offer, SlipAlign.Center, SlipStyle.DoubleHeight));
        else if (options.ShowPriceOnSenha)
            lines.Add(new SlipTextLine(Money.Format(slip.PriceCents), SlipAlign.Center, SlipStyle.DoubleHeight));

        if (options.ShowRules) lines.Add(new SlipTextLine(""));

        var stamp = options.ShowDate
            ? $"{slip.Reference}   {slip.CreatedAt:dd/MM HH:mm}"
            : slip.Reference;

        // Without a line of its own, the group name joins the reference rather
        // than disappearing - the bar still needs to know the slip is theirs.
        if (!string.IsNullOrWhiteSpace(slip.PrintGroup) && !options.ShowRules)
            stamp = $"{stamp}  {slip.PrintGroup.ToUpperInvariant()}";

        lines.Add(new SlipTextLine(stamp, SlipAlign.Center));

        if (options.ShowRules)
        {
            if (!string.IsNullOrWhiteSpace(slip.PrintGroup))
                lines.Add(new SlipTextLine(slip.PrintGroup.ToUpperInvariant(), SlipAlign.Center));

            lines.Add(new SlipTextLine(Layout.Rule('=', options.Columns)));
        }

        return lines;
    }

    private static void AddHeader(List<SlipTextLine> lines, TicketOptions options)
    {
        // An empty header is how the header is turned off - one piece of state
        // rather than a switch and a text box that can disagree.
        if (!string.IsNullOrWhiteSpace(options.Header))
            lines.Add(new SlipTextLine(options.Header, SlipAlign.Center, SlipStyle.Bold));

        AddRule(lines, '=', options);
    }

    private static void AddFooter(List<SlipTextLine> lines, TicketOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Footer))
            lines.Add(new SlipTextLine(options.Footer, SlipAlign.Center));
    }

    private static void AddRule(List<SlipTextLine> lines, char character, TicketOptions options)
    {
        if (options.ShowRules) lines.Add(new SlipTextLine(Layout.Rule(character, options.Columns)));
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
