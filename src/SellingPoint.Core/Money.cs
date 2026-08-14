using System.Globalization;

namespace SellingPoint.Core;

/// <summary>
/// Every amount in the app is an integer number of cents. Formatting and parsing
/// are done by hand rather than through <see cref="CultureInfo"/> because the app
/// runs in globalization-invariant mode - there is no pt-PT culture to ask, and a
/// culture-aware currency format would produce "$1.50". Doing it by hand also
/// means the ticket, the screen and the CSV agree on every machine, and that the
/// separator is a plain space rather than the U+00A0 a culture would insert.
/// </summary>
public static class Money
{
    /// <summary>e.g. 150 => "1,50 EUR sign". Negative amounts keep the sign in front.</summary>
    public static string Format(int cents) => FormatPlain(cents) + " €";

    /// <summary>Same as <see cref="Format"/> without the currency sign: 150 => "1,50".</summary>
    public static string FormatPlain(int cents)
    {
        var sign = cents < 0 ? "-" : "";
        var abs = Math.Abs((long)cents);
        var euros = (abs / 100).ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{sign}{euros},{abs % 100:00}";
    }

    /// <summary>
    /// Parses what a person actually types into a price box: "1,50", "1.50", "2",
    /// "1,50 EUR sign". Both separators are accepted as the decimal point, but only one
    /// may appear - nobody types a thousands separator into the price of a beer,
    /// and guessing which one they meant is how a 1.500 EUR beer happens.
    /// </summary>
    public static bool TryParseEuros(string? text, out int cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cleaned = text.Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        if (cleaned.Length == 0) return false;
        if (cleaned.Count(c => c is ',' or '.') > 1) return false;

        cleaned = cleaned.Replace(',', '.');
        if (!decimal.TryParse(cleaned,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var euros))
            return false;

        var scaled = Math.Round(euros * 100m, MidpointRounding.AwayFromZero);
        if (scaled is > int.MaxValue or < int.MinValue) return false;

        cents = (int)scaled;
        return true;
    }
}
