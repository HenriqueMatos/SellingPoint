using Dapper;

namespace SellingPoint.Data;

/// <summary>
/// Key/value settings. A table rather than columns so adding a setting later
/// never needs a schema change.
/// </summary>
public sealed class SettingsRepository(Db db)
{
    public string? Get(string key)
    {
        using var c = db.Open();
        return c.ExecuteScalar<string?>("SELECT value FROM setting WHERE key = @key", new { key });
    }

    public T Get<T>(string key, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(Get(key), ignoreCase: true, out var parsed) ? parsed : fallback;

    public int GetInt(string key, int fallback)
        => int.TryParse(Get(key), out var parsed) ? parsed : fallback;

    public bool GetBool(string key, bool fallback)
        => bool.TryParse(Get(key), out var parsed) ? parsed : fallback;

    public string GetString(string key, string fallback) => Get(key) ?? fallback;

    public void Set(string key, object? value)
    {
        using var c = db.Open();
        c.Execute(
            """
            INSERT INTO setting(key, value) VALUES(@key, @text)
            ON CONFLICT(key) DO UPDATE SET value = @text
            """,
            new { key, text = value?.ToString() ?? "" });
    }
}

/// <summary>Setting keys in one place, so a typo is a compile error.</summary>
public static class SettingKeys
{
    public const string OutOfStockBehaviour = "out_of_stock_behaviour";
    public const string PrinterTransport = "printer_transport";
    public const string PrinterTarget = "printer_target";
    public const string PrinterBaudRate = "printer_baud_rate";
    /// <summary>Kept only to migrate older databases; the app writes the two below.</summary>
    public const string PaperColumns = "paper_columns";
    public const string PaperWidth = "paper_width";
    public const string TicketFontSize = "ticket_font_size";
    public const string TicketHeader = "ticket_header";
    public const string TicketFooter = "ticket_footer";
    public const string ShowPriceOnSenha = "show_price_on_senha";
    public const string ShowRules = "show_rules";
    public const string ShowDate = "show_date";
    public const string ShowTotalOnGroupSlip = "show_total_group";
    public const string ShowPricesOnGroupSlip = "show_prices_group";
    public const string LineSpacingDots = "line_spacing_dots";
    public const string FeedLinesBeforeCut = "feed_lines_before_cut";
    public const string FoldAccents = "fold_accents";
    public const string OpenCashDrawer = "open_cash_drawer";
    public const string PrintSummarySlip = "print_summary_slip";
    public const string AdminPin = "admin_pin";
    public const string CodePage = "code_page";
}
