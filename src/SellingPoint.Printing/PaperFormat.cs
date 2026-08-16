namespace SellingPoint.Printing;

public enum PaperWidth
{
    /// <summary>80 mm roll.</summary>
    Wide,

    /// <summary>58 mm roll.</summary>
    Narrow
}

/// <summary>
/// The sizes offered, smallest first. The names are what gets written to the
/// settings table, so a member may be added but never renamed: renaming would
/// silently drop the size every existing till is already set to.
/// </summary>
public enum TicketFontSize
{
    /// <summary>ESC/POS font B. Smallest, most characters per line.</summary>
    Small,

    /// <summary>ESC/POS font A. The usual receipt size.</summary>
    Normal,

    /// <summary>Font A at double width and height. Half the characters per line.</summary>
    Large,

    /// <summary>
    /// Font B doubled. At 18x34 dots it lands between font A plain and font A
    /// doubled, filling what was a jump straight from 48 columns to 24.
    /// </summary>
    Medium,

    /// <summary>Font A at triple size. As large as is any use on a till roll.</summary>
    Huge
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
    /// <summary>The sizes offered, smallest letters first. Drives the settings list.</summary>
    public static IReadOnlyList<TicketFontSize> InSizeOrder =>
    [
        TicketFontSize.Small, TicketFontSize.Normal, TicketFontSize.Medium,
        TicketFontSize.Large, TicketFontSize.Huge
    ];

    /// <summary>Dots across the printable area. 203 dpi is 8 dots to the millimetre.</summary>
    public static int PrintableDots(PaperWidth paper) => paper == PaperWidth.Wide ? 576 : 384;

    /// <summary>One character cell before any multiplier: font A is 12x24, font B 9x17.</summary>
    public static int CellWidthDots(TicketFontSize font) => FontCommand(font) == 1 ? 9 : 12;

    public static int CellHeightDots(TicketFontSize font) => FontCommand(font) == 1 ? 17 : 24;

    /// <summary>
    /// Characters per line: how many of this size's cells fit across the paper.
    ///
    /// Divided rather than tabulated. A hand-written table can disagree with the
    /// multipliers beside it - and a number that disagrees with the size it
    /// describes is exactly how a line ends up wider than the paper.
    /// </summary>
    public static int Columns(PaperWidth paper, TicketFontSize font)
        => PrintableDots(paper) / (CellWidthDots(font) * WidthMultiplier(font));

    /// <summary>ESC M n - 0 selects font A, 1 selects the smaller font B.</summary>
    public static byte FontCommand(TicketFontSize font)
        => font is TicketFontSize.Small or TicketFontSize.Medium ? (byte)1 : (byte)0;

    /// <summary>How many times wider and taller than its own font's cell this prints.</summary>
    public static int WidthMultiplier(TicketFontSize font) => font switch
    {
        TicketFontSize.Medium or TicketFontSize.Large => 2,
        TicketFontSize.Huge => 3,
        _ => 1
    };

    public static int HeightMultiplier(TicketFontSize font) => WidthMultiplier(font);

    /// <summary>
    /// How wide this line's glyphs actually come out, base size and the line's own
    /// emphasis together.
    ///
    /// Taken as the larger of the two, never multiplied. A senha doubles its
    /// product name for legibility; at a base size that is already doubled,
    /// multiplying would give four-times-wide letters and a line four times too
    /// long for the paper.
    ///
    /// This lives here rather than inside the encoder because three places need
    /// the same answer - the bytes, the preview panel and the paper estimate - and
    /// when the preview worked it out for itself it got it wrong at every doubled
    /// size, drawing product names at half the width the paper gives them.
    /// </summary>
    public static int EffectiveWidth(SlipStyle style, TicketFontSize font)
        => Math.Max(WidthMultiplier(font), style.HasFlag(SlipStyle.DoubleWidth) ? 2 : 1);

    public static int EffectiveHeight(SlipStyle style, TicketFontSize font)
        => Math.Max(HeightMultiplier(font), style.HasFlag(SlipStyle.DoubleHeight) ? 2 : 1);

    /// <summary>
    /// How many characters of this particular line fit across the paper. Columns
    /// already accounts for the base size, so only emphasis beyond it narrows the
    /// line any further.
    /// </summary>
    public static int UsableColumns(PaperWidth paper, TicketFontSize font, SlipStyle style)
        => PrintableDots(paper) / (CellWidthDots(font) * EffectiveWidth(style, font));

    /// <summary>
    /// How much wider and taller this line comes out than an ordinary line of font
    /// A - the numbers the preview panel draws with, so what is on screen is the
    /// shape of what leaves the print head rather than an impression of it.
    /// </summary>
    public static (double Width, double Height) SizeRelativeToNormal(SlipStyle style, TicketFontSize font)
        => (CellWidthDots(font) * EffectiveWidth(style, font) / 12.0,
            CellHeightDots(font) * EffectiveHeight(style, font) / 24.0);

    public static string Describe(PaperWidth paper, TicketFontSize font)
        => $"{(paper == PaperWidth.Wide ? "80" : "58")} mm · {Name(font)} · {Columns(paper, font)} colunas";

    public static string Name(TicketFontSize font) => font switch
    {
        TicketFontSize.Small => "letra pequena",
        TicketFontSize.Medium => "letra média",
        TicketFontSize.Large => "letra grande",
        TicketFontSize.Huge => "letra muito grande",
        _ => "letra normal"
    };
}
