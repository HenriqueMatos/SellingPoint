using SellingPoint.Core;

namespace SellingPoint.Printing;

/// <summary>
/// Turns one sale into the pieces of paper it produces. Pure - no I/O, no printer,
/// no clock. This is where the "drinks and desserts together, food separately"
/// requirement actually lives.
/// </summary>
public static class TicketBuilder
{
    public static IReadOnlyList<Slip> Build(Sale sale, TicketOptions options)
    {
        var slips = new List<Slip>();
        if (sale.Lines.Count == 0) return slips;

        var reference = Reference(sale.TicketNumber);
        var senhaCounter = 0;

        // Said on every piece of paper the sale produces, not just the customer's
        // copy: whoever hands the item over at the bar is the one who would
        // otherwise wait for money that is not coming.
        var offer = sale.PaymentMethod == PaymentMethod.Offer;

        // GroupBy preserves the order the groups first appear in, so the same sale
        // always prints in the same order.
        foreach (var group in sale.Lines.GroupBy(l => l.PrintGroup))
        {
            var grouped = group.Where(l => l.SlipMode == SlipMode.Grouped).ToList();
            if (grouped.Count > 0)
            {
                slips.Add(new GroupedSlip(
                    group.Key, reference, sale.CreatedAt,
                    grouped.Select(ToItem).ToList(),
                    grouped.Sum(l => l.LineTotalCents),
                    IsOffer: offer));
            }

            // One slip per unit. The counter runs across the whole sale so no two
            // senhas from one order carry the same number.
            foreach (var line in group.Where(l => l.SlipMode == SlipMode.PerUnit))
            {
                for (var i = 0; i < line.Qty; i++)
                {
                    slips.Add(new SenhaSlip(
                        group.Key, $"{reference}-{++senhaCounter}", sale.CreatedAt,
                        line.ProductName, line.UnitPriceCents, offer));
                }
            }
        }

        if (options.PrintSummarySlip)
        {
            slips.Add(new GroupedSlip(
                "", reference, sale.CreatedAt,
                sale.Lines.Select(ToItem).ToList(),
                sale.TotalCents,
                IsSummary: true,
                IsOffer: offer));
        }

        return slips;
    }

    public static string Reference(int ticketNumber) => $"#{ticketNumber:0000}";

    private static SlipItem ToItem(SaleLine line) => new(line.Qty, line.ProductName, line.LineTotalCents);
}
