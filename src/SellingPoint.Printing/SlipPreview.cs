using System.Text;

namespace SellingPoint.Printing;

/// <summary>
/// The plain-text form of a slip, for the preview panel on the till and for the
/// files <see cref="FileTransport"/> writes during development.
/// </summary>
public static class SlipPreview
{
    public static string ToText(IEnumerable<SlipTextLine> lines, TicketOptions options)
    {
        var text = new StringBuilder();
        var columns = options.Columns;

        foreach (var line in lines)
        {
            // How many characters this line has room for, asked of the same place
            // the printer's own size command is built from. Working it out here
            // instead is what used to draw product names at half their width at
            // every doubled size.
            var usable = PaperFormat.UsableColumns(options.Paper, options.FontSize, line.Style);
            var content = Layout.Truncate(line.Text, usable);

            text.AppendLine(line.Align switch
            {
                SlipAlign.Center => Layout.Center(content, usable),
                SlipAlign.Right => content.PadLeft(usable),
                _ => content
            });
        }

        return text.ToString();
    }

    public static string ToText(Slip slip, TicketOptions options)
        => ToText(SlipRenderer.Render(slip, options), options);

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
