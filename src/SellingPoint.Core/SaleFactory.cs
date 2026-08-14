namespace SellingPoint.Core;

public static class SaleFactory
{
    /// <summary>
    /// Turns the cart into a <see cref="Sale"/> with every line snapshotted.
    /// Id and TicketNumber stay zero - the repository assigns them on insert.
    /// </summary>
    public static Sale Build(
        Cart cart,
        IReadOnlyDictionary<int, Category> categoriesById,
        PaymentMethod method,
        int cashReceivedCents,
        DateTime now)
    {
        if (cart.IsEmpty)
            throw new InvalidOperationException("Cannot complete a sale with an empty cart.");

        var total = cart.TotalCents;
        var change = 0;

        if (method == PaymentMethod.Cash)
        {
            if (!Tender.TryChange(total, cashReceivedCents, out change))
                throw new InvalidOperationException(
                    $"Cash received ({Money.Format(cashReceivedCents)}) is less than the total ({Money.Format(total)}).");
        }
        else
        {
            cashReceivedCents = 0;
        }

        var sale = new Sale
        {
            CreatedAt = now,
            TotalCents = total,
            PaymentMethod = method,
            CashReceivedCents = cashReceivedCents,
            ChangeCents = change
        };

        foreach (var line in cart.Lines)
        {
            var category = categoriesById.GetValueOrDefault(line.Product.CategoryId);

            sale.Lines.Add(new SaleLine
            {
                ProductId = line.Product.Id,
                ProductName = line.Product.Name,
                UnitPriceCents = line.UnitPriceCents,
                CategoryName = category?.Name ?? "",
                PrintGroup = category?.PrintGroup ?? "Bar",
                SlipMode = category?.SlipMode ?? SlipMode.Grouped,
                Qty = line.Qty,
                LineTotalCents = line.LineTotalCents
            });
        }

        return sale;
    }
}
