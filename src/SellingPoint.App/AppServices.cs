using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App;

/// <summary>
/// The composition root. Six objects wired by hand - a container would be more
/// ceremony than the whole application.
/// </summary>
public sealed class AppServices
{
    public Db Db { get; }
    public CatalogRepository Catalog { get; }
    public SalesRepository Sales { get; }
    public SettingsRepository Settings { get; }
    public TicketPrinter Printer { get; }

    public AppServices(string? databasePath = null)
    {
        Db = new Db(databasePath ?? Db.DefaultPath());
        Db.Initialize();

        Catalog = new CatalogRepository(Db);
        Sales = new SalesRepository(Db);
        Settings = new SettingsRepository(Db);
        Printer = new TicketPrinter(BuildTransport(), BuildTicketOptions());
    }

    /// <summary>Where the file transport drops slips when no printer is configured.</summary>
    public string DefaultTicketFolder =>
        Path.Combine(Path.GetDirectoryName(Db.Path) ?? ".", "talões");

    public OutOfStockBehaviour OutOfStock =>
        Settings.Get(SettingKeys.OutOfStockBehaviour, OutOfStockBehaviour.Warn);

    /// <summary>Re-reads every printer setting. Called when Settings are saved.</summary>
    public void ReloadPrinter()
    {
        Printer.Options = BuildTicketOptions();
        Printer.Transport = BuildTransport();
    }

    public TicketOptions BuildTicketOptions() => new()
    {
        Columns = Settings.GetInt(SettingKeys.PaperColumns, 48),
        Header = Settings.GetString(SettingKeys.TicketHeader, ""),
        Footer = Settings.GetString(SettingKeys.TicketFooter, "Obrigado!"),
        ShowPriceOnSenha = Settings.GetBool(SettingKeys.ShowPriceOnSenha, true),
        PrintSummarySlip = Settings.GetBool(SettingKeys.PrintSummarySlip, false),
        CodePage = Settings.GetInt(SettingKeys.CodePage, 858),
        FoldAccents = Settings.GetBool(SettingKeys.FoldAccents, false),
        OpenCashDrawer = Settings.GetBool(SettingKeys.OpenCashDrawer, false)
    };

    public IPrintTransport BuildTransport()
    {
        var target = Settings.GetString(SettingKeys.PrinterTarget, "");

        return Settings.GetString(SettingKeys.PrinterTransport, "file") switch
        {
            "network" => new NetworkTransport(target),
            "serial" => new SerialTransport(target),
            "windows" => new WindowsRawTransport(target),
            _ => new FileTransport(string.IsNullOrWhiteSpace(target) ? DefaultTicketFolder : target)
        };
    }
}
