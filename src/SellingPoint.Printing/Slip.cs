namespace SellingPoint.Printing;

/// <summary>One printed piece of paper.</summary>
public abstract record Slip(string PrintGroup, string Reference, DateTime CreatedAt);

public sealed record SlipItem(int Qty, string Name, int TotalCents);

/// <summary>
/// The slip for one print group: every item that group owes, listed with quantities.
/// Also used for the optional customer summary, where <see cref="IsSummary"/> is set
/// and the group name is dropped.
/// </summary>
public sealed record GroupedSlip(
    string PrintGroup,
    string Reference,
    DateTime CreatedAt,
    IReadOnlyList<SlipItem> Items,
    int TotalCents,
    bool IsSummary = false) : Slip(PrintGroup, Reference, CreatedAt);

/// <summary>One unit, one slip - the senha the bar collects when it hands the item over.</summary>
public sealed record SenhaSlip(
    string PrintGroup,
    string Reference,
    DateTime CreatedAt,
    string ItemName,
    int PriceCents) : Slip(PrintGroup, Reference, CreatedAt);

public enum SlipAlign
{
    Left,
    Center,
    Right
}

[Flags]
public enum SlipStyle
{
    None = 0,
    Bold = 1,
    DoubleHeight = 2,
    DoubleWidth = 4
}

/// <summary>
/// One line of a slip, before it becomes either preview text or printer bytes.
/// Alignment is a property of the line rather than baked-in padding, because the
/// printer centres far more reliably than space-padding does once a line is bold
/// or double width.
/// </summary>
public sealed record SlipTextLine(string Text, SlipAlign Align = SlipAlign.Left, SlipStyle Style = SlipStyle.None);

public sealed record TicketOptions
{
    /// <summary>Characters per line: 48 on 80mm paper, 32 on 58mm.</summary>
    public int Columns { get; init; } = 48;

    public string Header { get; init; } = "";
    public string Footer { get; init; } = "Obrigado!";

    /// <summary>Some organizers deliberately hide the price on the slip the bar collects.</summary>
    public bool ShowPriceOnSenha { get; init; } = true;

    /// <summary>An extra slip listing the whole order, for the customer to keep.</summary>
    public bool PrintSummarySlip { get; init; }

    /// <summary>
    /// 858 is the default because it is the only common thermal-printer code page
    /// with both the Portuguese accents and the euro sign. CP860 is "the Portuguese
    /// one" but predates the euro, so a price on it prints as "1,50 E".
    /// </summary>
    public int CodePage { get; init; } = 858;

    /// <summary>Strip accents before encoding. For printers whose code page has none.</summary>
    public bool FoldAccents { get; init; }

    public bool OpenCashDrawer { get; init; }
}
