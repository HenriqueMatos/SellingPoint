namespace SellingPoint.Core;

/// <summary>How a category's items turn into paper.</summary>
public enum SlipMode
{
    /// <summary>One line per product on the group's slip: "3x Cerveja".</summary>
    Grouped,

    /// <summary>One slip per unit sold - the classic senha the bar collects.</summary>
    PerUnit
}

public enum PaymentMethod
{
    Cash,
    Card
}

/// <summary>What the till does when a stock-tracked product hits zero.</summary>
public enum OutOfStockBehaviour
{
    /// <summary>Sell anyway and flag it. A bar that finds three more crates in the
    /// van should not be locked out by the software.</summary>
    Warn,

    /// <summary>Refuse the sale.</summary>
    Block
}

public enum AddResult
{
    Added,
    AddedBeyondStock,
    Blocked
}
