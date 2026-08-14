using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>One slip, encoded and ready to send.</summary>
public sealed record ComposedSlip(string Title, byte[] Payload, string Preview);

/// <summary>
/// Goes from a sale to paper: build the slips, lay each one out, encode it, hand
/// it to the transport. Composing and sending are separate so slips can be put in
/// a queue when no printer is answering.
/// </summary>
public sealed class TicketPrinter(IPrintTransport transport, TicketOptions options)
{
    public IPrintTransport Transport { get; set; } = transport;
    public TicketOptions Options { get; set; } = options;

    /// <summary>Encodes every slip the sale produces, without touching the printer.</summary>
    public IReadOnlyList<ComposedSlip> Compose(Sale sale)
    {
        var slips = TicketBuilder.Build(sale, Options);
        var composed = new List<ComposedSlip>(slips.Count);

        for (var i = 0; i < slips.Count; i++)
        {
            var lines = SlipRenderer.Render(slips[i], Options);
            // The drawer opens once per sale, not once per slip.
            var drawer = i == 0 && Options.OpenCashDrawer;

            composed.Add(new ComposedSlip(
                Describe(slips[i]),
                EscPosEncoder.Encode(lines, Options, drawer),
                SlipPreview.ToText(lines, Options.Columns)));
        }

        return composed;
    }

    /// <summary>
    /// A plain block of text - the closing summary the organizer staples to the
    /// cash bag, and the Settings test print.
    /// </summary>
    public ComposedSlip ComposeText(string title, IEnumerable<string> body)
    {
        var lines = new List<SlipTextLine>
        {
            new(title, SlipAlign.Center, SlipStyle.Bold | SlipStyle.DoubleHeight),
            new(Layout.Rule('=', Options.Columns))
        };

        lines.AddRange(body.Select(line => new SlipTextLine(line)));
        lines.Add(new SlipTextLine(Layout.Rule('=', Options.Columns)));

        return new ComposedSlip(title, EscPosEncoder.Encode(lines, Options),
            SlipPreview.ToText(lines, Options.Columns));
    }

    public void Send(ComposedSlip slip) => Transport.Send(slip.Payload, slip.Preview);

    /// <summary>Prints every slip the sale produces straight away. Returns how many came out.</summary>
    public int Print(Sale sale)
    {
        var composed = Compose(sale);
        foreach (var slip in composed) Send(slip);
        return composed.Count;
    }

    public void PrintText(string title, IEnumerable<string> body) => Send(ComposeText(title, body));

    /// <summary>What the sale would print, as text. Drives the preview panel on the till.</summary>
    public string Preview(Sale sale) => SlipPreview.ToText(TicketBuilder.Build(sale, Options), Options);

    /// <summary>
    /// Settings test print. The accent line is the point of it: if those come out
    /// as line-drawing characters, the code page is wrong.
    /// </summary>
    public ComposedSlip ComposeTest() => ComposeText("TESTE DE IMPRESSÃO",
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

    public void PrintTest() => Send(ComposeTest());

    /// <summary>What the operator sees for this slip in the print queue.</summary>
    private static string Describe(Slip slip) => slip switch
    {
        GroupedSlip { IsSummary: true } summary => $"{summary.Reference} — Resumo",
        GroupedSlip grouped => $"{grouped.Reference} — {grouped.PrintGroup}",
        SenhaSlip senha => $"{senha.Reference} — {senha.ItemName}",
        _ => slip.Reference
    };
}
