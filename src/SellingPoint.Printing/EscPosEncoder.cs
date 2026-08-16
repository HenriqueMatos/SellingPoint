using System.Text;

namespace SellingPoint.Printing;

/// <summary>
/// Turns laid-out lines into the raw bytes a thermal printer understands. ESC/POS
/// is a binary protocol, not a driver format - these bytes go straight down a
/// serial port, a TCP socket, or the Windows raw spooler.
/// </summary>
public static class EscPosEncoder
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte Lf = 0x0A;

    /// <summary>
    /// ESC t takes the printer's own slot number, not the Windows code page number.
    /// Slot 19 (CP858) is the default: it carries the Portuguese accents and the
    /// euro sign. Getting this wrong is what turns accented product names into
    /// line-drawing characters on the paper.
    /// </summary>
    private static readonly Dictionary<int, byte> CodePageSlots = new()
    {
        [437] = 0, [850] = 2, [860] = 3, [863] = 4, [865] = 5, [1252] = 16, [858] = 19
    };

    static EscPosEncoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static byte[] Encode(IEnumerable<SlipTextLine> lines, TicketOptions options, bool openCashDrawer = false)
    {
        var encoding = GetEncoding(options.CodePage);
        var spellEuro = !CanEncodeEuro(encoding);
        using var stream = new MemoryStream();

        Write(stream, Esc, (byte)'@');
        if (CodePageSlots.TryGetValue(options.CodePage, out var slot))
            Write(stream, Esc, (byte)'t', slot);

        // ESC M picks the typeface. Font B is narrower, which is what buys the
        // extra columns at the small size.
        Write(stream, Esc, (byte)'M', PaperFormat.FontCommand(options.FontSize));

        // ESC 3 n sets the line height in dots. The printer default is 30; going
        // to 24 takes a fifth off every line on every ticket without removing a
        // single thing from what is printed.
        if (options.LineSpacingDots is > 0 and <= 255)
            Write(stream, Esc, (byte)'3', (byte)options.LineSpacingDots);

        foreach (var line in lines)
        {
            Write(stream, Esc, (byte)'a', Alignment(line.Align));
            Write(stream, Esc, (byte)'E', (byte)(line.Style.HasFlag(SlipStyle.Bold) ? 1 : 0));
            Write(stream, Gs, (byte)'!', Size(line.Style, options.FontSize));

            var text = options.FoldAccents ? Accents.Fold(line.Text) : line.Text;
            // A single 'E' rather than the '?' the fallback would give, and rather
            // than "EUR", which is three characters wide and would shunt every
            // right-aligned price off the end of an already-padded line.
            if (spellEuro) text = text.Replace('€', 'E');

            var bytes = encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(Lf);
        }

        // Back to plain text, feed the slip clear of the cutter, then cut. The
        // feed is the single largest fixed cost per ticket, and the one that
        // cannot simply be minimised: the blade sits above the print head, so too
        // few lines cuts through the last line of text.
        Write(stream, Esc, (byte)'a', 0);
        Write(stream, Esc, (byte)'E', 0);
        Write(stream, Gs, (byte)'!', Size(SlipStyle.None, options.FontSize));
        Write(stream, Esc, (byte)'d', (byte)Math.Clamp(options.FeedLinesBeforeCut, 0, 255));
        Write(stream, Gs, (byte)'V', 66, 0);

        if (openCashDrawer)
            Write(stream, Esc, (byte)'p', 0, 25, 250);

        return stream.ToArray();
    }

    /// <summary>
    /// GS ! packs the width multiplier into the high nibble and the height into the
    /// low one. The multipliers themselves come from <see cref="PaperFormat"/>,
    /// which is also where the preview panel and the paper estimate read them, so
    /// the three cannot drift apart.
    /// </summary>
    private static byte Size(SlipStyle style, TicketFontSize font)
        => (byte)(((PaperFormat.EffectiveWidth(style, font) - 1) << 4)
                  | (PaperFormat.EffectiveHeight(style, font) - 1));

    private static byte Alignment(SlipAlign align) => align switch
    {
        SlipAlign.Center => 1,
        SlipAlign.Right => 2,
        _ => 0
    };

    private static Encoding GetEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage,
                new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.ASCII;
        }
    }

    /// <summary>The DOS code pages that predate the euro encode it as '?'.</summary>
    private static bool CanEncodeEuro(Encoding encoding) => encoding.GetBytes("€") is not [0x3F];

    private static void Write(Stream stream, params byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}

/// <summary>
/// Accent stripping for printers whose code page has none. Done with an explicit
/// map rather than Unicode normalisation because the app runs in globalization
/// invariant mode, where <see cref="string.Normalize(NormalizationForm)"/> is a no-op.
/// </summary>
public static class Accents
{
    private const string Accented = "áàâãäéèêëíìîïóòôõöúùûüçñÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑ";
    private const string Plain    = "aaaaaeeeeiiiiooooouuuucnAAAAAEEEEIIIIOOOOOUUUUCN";

    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var folded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var index = Accented.IndexOf(character);
            folded.Append(index >= 0 ? Plain[index] : character);
        }

        return folded.ToString();
    }
}
