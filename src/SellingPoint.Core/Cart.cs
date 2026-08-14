namespace SellingPoint.Core;

public sealed class CartLine
{
    public required Product Product { get; init; }

    /// <summary>Snapshotted when the line is created, so an admin editing prices
    /// mid-order cannot change what is already on screen.</summary>
    public required int UnitPriceCents { get; init; }

    public int Qty { get; set; }

    public int LineTotalCents => UnitPriceCents * Qty;
}

/// <summary>
/// The order being rung up. Holds the stock rule so it lives in one tested place
/// rather than in whichever button handler remembered to check.
/// </summary>
public sealed class Cart
{
    private readonly List<CartLine> _lines = [];

    public OutOfStockBehaviour OutOfStock { get; set; } = OutOfStockBehaviour.Warn;

    public IReadOnlyList<CartLine> Lines => _lines;
    public int TotalCents => _lines.Sum(l => l.LineTotalCents);
    public int ItemCount => _lines.Sum(l => l.Qty);
    public bool IsEmpty => _lines.Count == 0;

    /// <summary>
    /// Adds a product, or increments the existing line for it. Stock is checked
    /// against the resulting quantity, not the added one, so tapping a product
    /// six times cannot slip past a stock of five.
    /// </summary>
    public AddResult Add(Product product, int qty = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);

        var line = Find(product);
        var desired = (line?.Qty ?? 0) + qty;
        var result = AddResult.Added;

        if (product.TrackStock && desired > product.StockQty)
        {
            if (OutOfStock == OutOfStockBehaviour.Block) return AddResult.Blocked;
            result = AddResult.AddedBeyondStock;
        }

        if (line is null)
            _lines.Add(new CartLine { Product = product, UnitPriceCents = product.PriceCents, Qty = qty });
        else
            line.Qty = desired;

        return result;
    }

    /// <summary>The "-" button. Never blocked; removes the line at zero.</summary>
    public void Decrement(Product product)
    {
        var line = Find(product);
        if (line is null) return;

        line.Qty--;
        if (line.Qty <= 0) _lines.Remove(line);
    }

    public void Remove(Product product)
    {
        var line = Find(product);
        if (line is not null) _lines.Remove(line);
    }

    public void Clear() => _lines.Clear();

    private CartLine? Find(Product product) => _lines.FirstOrDefault(l => l.Product.Id == product.Id);
}
