namespace SellingPoint.Core;

public static class Tender
{
    private static readonly int[] Notes = [500, 1000, 2000, 5000];

    /// <summary>
    /// Change owed for a cash sale. Returns false when the customer handed over
    /// less than the total, in which case the sale must not complete.
    /// </summary>
    public static bool TryChange(int totalCents, int receivedCents, out int changeCents)
    {
        changeCents = receivedCents - totalCents;
        if (changeCents >= 0) return true;

        changeCents = 0;
        return false;
    }

    /// <summary>
    /// Buttons for the cash numpad: the exact amount, the next round 5 EUR, and the
    /// notes a customer is likely to hand over. Four at most - the operator is
    /// standing in front of a queue.
    /// </summary>
    public static IReadOnlyList<int> QuickTender(int totalCents)
    {
        if (totalCents <= 0) return [0];

        var nextRound = (totalCents + 499) / 500 * 500;

        return new[] { totalCents, nextRound }
            .Concat(Notes.Where(n => n > totalCents))
            .Distinct()
            .Order()
            .Take(4)
            .ToArray();
    }
}
