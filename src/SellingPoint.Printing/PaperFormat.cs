namespace SellingPoint.Printing;

public enum PaperWidth
{
    /// <summary>80 mm roll.</summary>
    Wide,

    /// <summary>58 mm roll.</summary>
    Narrow
}

public enum TicketFontSize
{
    /// <summary>ESC/POS font B. Smallest, most characters per line.</summary>
    Small,

    /// <summary>ESC/POS font A. The usual receipt size.</summary>
    Normal,

    /// <summary>Font A at double width and height. Half the characters per line.</summary>
    Large
}

/// <summary>
/// On a thermal printer the letter size and the number of characters per line are
/// the same thing, not two settings. Bigger letters mean fewer of them.
///
/// This is why the column count is derived here and never typed in: a hand-entered
/// 48 alongside a doubled font is exactly how every line overflows and the price
/// column stops lining up.
/// </summary>
public static class PaperFormat
{
    /// <summary>Characters per line for a paper width and letter size.</summary>
    public static int Columns(PaperWidth paper, TicketFontSize font) => (paper, font) switch
    {
        (PaperWidth.Wide, TicketFontSize.Small) => 64,
        (PaperWidth.Wide, TicketFontSize.Normal) => 48,
        (PaperWidth.Wide, TicketFontSize.Large) => 24,

        (PaperWidth.Narrow, TicketFontSize.Small) => 42,
        (PaperWidth.Narrow, TicketFontSize.Normal) => 32,
        (PaperWidth.Narrow, TicketFontSize.Large) => 16,

        _ => 48
    };

    /// <summary>ESC M n - 0 selects font A, 1 selects the smaller font B.</summary>
    public static byte FontCommand(TicketFontSize font) => font == TicketFontSize.Small ? (byte)1 : (byte)0;

    /// <summary>How many times wider and taller than font A's own size this prints.</summary>
    public static int WidthMultiplier(TicketFontSize font) => font == TicketFontSize.Large ? 2 : 1;

    public static int HeightMultiplier(TicketFontSize font) => font == TicketFontSize.Large ? 2 : 1;

    public static string Describe(PaperWidth paper, TicketFontSize font)
        => $"{(paper == PaperWidth.Wide ? "80" : "58")} mm · {Name(font)} · {Columns(paper, font)} colunas";

    public static string Name(TicketFontSize font) => font switch
    {
        TicketFontSize.Small => "letra pequena",
        TicketFontSize.Large => "letra grande",
        _ => "letra normal"
    };
}
