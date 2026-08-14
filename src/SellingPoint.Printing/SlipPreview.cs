using System.Text;

namespace SellingPoint.Printing;

/// <summary>
/// The plain-text form of a slip, for the preview panel on the till and for the
/// files <see cref="FileTransport"/> writes during development.
/// </summary>
public static class SlipPreview
{
    public static string ToText(IEnumerable<SlipTextLine> lines, int columns)
    {
        var text = new StringBuilder();

        foreach (var line in lines)
        {
            // Double-width glyphs cover twice the paper, so the printer lays them
            // out against half the column count. Centring at full width here puts
            // them in the same visual place.
            var usable = line.Style.HasFlag(SlipStyle.DoubleWidth) ? columns / 2 : columns;
            var content = Layout.Truncate(line.Text, usable);

            text.AppendLine(line.Align switch
            {
                SlipAlign.Center => Layout.Center(content, columns),
                SlipAlign.Right => content.PadLeft(columns),
                _ => content
            });
        }

        return text.ToString();
    }

    public static string ToText(Slip slip, TicketOptions options)
        => ToText(SlipRenderer.Render(slip, options), options.Columns);

    /// <summary>All of a sale's slips, separated by a scissors line.</summary>
    public static string ToText(IEnumerable<Slip> slips, TicketOptions options)
    {
        var text = new StringBuilder();
        var separator = new string('8', 1) + new string('<', options.Columns - 1);

        foreach (var slip in slips)
        {
            if (text.Length > 0) text.AppendLine(separator).AppendLine();
            text.Append(ToText(slip, options));
        }

        return text.ToString();
    }
}
