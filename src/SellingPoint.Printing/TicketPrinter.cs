using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>
/// The one place that goes from a sale to paper: build the slips, lay each one
/// out, encode it, hand it to the transport.
/// </summary>
public sealed class TicketPrinter(IPrintTransport transport, TicketOptions options)
{
    public IPrintTransport Transport { get; set; } = transport;
    public TicketOptions Options { get; set; } = options;

    /// <summary>Prints every slip the sale produces. Returns how many came out.</summary>
    public int Print(Sale sale)
    {
        var slips = TicketBuilder.Build(sale, Options);

        for (var i = 0; i < slips.Count; i++)
        {
            var lines = SlipRenderer.Render(slips[i], Options);
            // The drawer opens once per sale, not once per slip.
            var drawer = i == 0 && Options.OpenCashDrawer;
            Transport.Send(EscPosEncoder.Encode(lines, Options, drawer), SlipPreview.ToText(lines, Options.Columns));
        }

        return slips.Count;
    }

    /// <summary>What the sale would print, as text. Drives the preview panel on the till.</summary>
    public string Preview(Sale sale) => SlipPreview.ToText(TicketBuilder.Build(sale, Options), Options);

    /// <summary>
    /// A plain block of text - the closing summary the organizer staples to the cash
    /// bag, and the Settings test print.
    /// </summary>
    public void PrintText(string title, IEnumerable<string> body)
    {
        var lines = new List<SlipTextLine>
        {
            new(title, SlipAlign.Center, SlipStyle.Bold | SlipStyle.DoubleHeight),
            new(Layout.Rule('=', Options.Columns))
        };

        lines.AddRange(body.Select(line => new SlipTextLine(line)));
        lines.Add(new SlipTextLine(Layout.Rule('=', Options.Columns)));

        Transport.Send(EscPosEncoder.Encode(lines, Options), SlipPreview.ToText(lines, Options.Columns));
    }

    /// <summary>
    /// Settings test print. The accent line is the point of it: if those come out
    /// as line-drawing characters, the code page is wrong.
    /// </summary>
    public void PrintTest() => PrintText("TESTE DE IMPRESSAO",
    [
        Layout.LeftRight("Acentos", "áéíóú ãõ çÇ", Options.Columns),
        Layout.LeftRight("Colunas", Options.Columns.ToString(), Options.Columns),
        Layout.LeftRight("Code page", Options.CodePage.ToString(), Options.Columns),
        Layout.LeftRight("Impressora", Transport.Describe(), Options.Columns),
        "",
        Layout.Rule('-', Options.Columns),
        Layout.LeftRight("2x Cerveja", Money.Format(300), Options.Columns),
        Layout.LeftRight("TOTAL", Money.Format(300), Options.Columns)
    ]);
}
