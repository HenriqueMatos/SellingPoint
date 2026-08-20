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
    bool IsSummary = false,
    bool IsOffer = false) : Slip(PrintGroup, Reference, CreatedAt);

/// <summary>One unit, one slip - the senha the bar collects when it hands the item over.</summary>
public sealed record SenhaSlip(
    string PrintGroup,
    string Reference,
    DateTime CreatedAt,
    string ItemName,
    int PriceCents,
    bool IsOffer = false) : Slip(PrintGroup, Reference, CreatedAt);

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
    public PaperWidth Paper { get; init; } = PaperWidth.Wide;
    public TicketFontSize FontSize { get; init; } = TicketFontSize.Normal;

    /// <summary>
    /// Characters per line. Derived from the paper and the letter size rather than
    /// set, because on a thermal printer those are the same thing: a hand-entered
    /// column count that disagrees with the font is exactly how every line
    /// overflows and the price column stops lining up.
    /// </summary>
    public int Columns => PaperFormat.Columns(Paper, FontSize);

    public string Header { get; init; } = "";
    public string Footer { get; init; } = "Obrigado!";

    /// <summary>Some organizers deliberately hide the price on the slip the bar collects.</summary>
    public bool ShowPriceOnSenha { get; init; } = true;

    // --- paper -------------------------------------------------------------
    // A slip of two items spends most of itself on decoration: rules, a date
    // line, a total, and the feed before the cut. Each of these is a line of
    // paper on every ticket of every order, all night.

    /// <summary>The = and - separator lines.</summary>
    public bool ShowRules { get; init; } = true;

    /// <summary>A line of its own for the date. Off, the time joins the reference line.</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>The group's subtotal. The bar handles no money, so it may not need it.</summary>
    public bool ShowTotalOnGroupSlip { get; init; } = true;

    /// <summary>Prices against each line of the group slip.</summary>
    public bool ShowPricesOnGroupSlip { get; init; } = true;

    /// <summary>
    /// ESC 3 n, in dots. The printer default is 30; 24 takes a fifth off every
    /// line without removing anything. Zero leaves the printer's own setting.
    /// </summary>
    public int LineSpacingDots { get; init; }

    /// <summary>
    /// Lines fed before the cut. The blade sits above the print head, so this is
    /// not free to shrink: too few and the cut goes through the last line. Which
    /// number is safe depends on the printer.
    /// </summary>
    public int FeedLinesBeforeCut { get; init; } = 4;

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
