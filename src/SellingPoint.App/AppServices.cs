using System.Net.Http;
using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App;

/// <summary>
/// The composition root. A handful of objects wired by hand - a container would be
/// more ceremony than the whole application.
/// </summary>
public sealed class AppServices : IDisposable
{
    public Db Db { get; }
    public CatalogRepository Catalog { get; }
    public SalesRepository Sales { get; }
    public SettingsRepository Settings { get; }
    public PrintQueueRepository PrintQueue { get; }
    public TicketPrinter Printer { get; }
    public PrintService Print { get; }

    public UpdateChecker Updates { get; }
    public UpdateInstaller Installer { get; }
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Where the app publishes its releases.</summary>
    public const string Repository = "HenriqueMatos/SellingPoint";

    public AppServices(string? databasePath = null)
    {
        Db = new Db(databasePath ?? Db.DefaultPath());
        Db.Initialize();

        Catalog = new CatalogRepository(Db);
        Sales = new SalesRepository(Db);
        Settings = new SettingsRepository(Db);
        PrintQueue = new PrintQueueRepository(Db);

        Printer = new TicketPrinter(BuildTransport(), BuildTicketOptions());
        Print = new PrintService(PrintQueue, Settings, Printer);
        Print.Start();

        Updates = new UpdateChecker(Http, Repository);
        Installer = new UpdateInstaller(Path.Combine(Path.GetDirectoryName(Db.Path) ?? ".", "atualizacao"));
    }

    /// <summary>Where the file transport drops slips when no printer is configured.</summary>
    public string DefaultTicketFolder =>
        Path.Combine(Path.GetDirectoryName(Db.Path) ?? ".", "talões");

    public OutOfStockBehaviour OutOfStock =>
        Settings.Get(SettingKeys.OutOfStockBehaviour, OutOfStockBehaviour.Warn);

    public int SerialBaudRate => Settings.GetInt(SettingKeys.PrinterBaudRate, 9600);

    /// <summary>Re-reads every printer setting. Called when Settings are saved.</summary>
    public void ReloadPrinter()
    {
        Printer.Options = BuildTicketOptions();
        Printer.Transport = BuildTransport();
        Print.RetryNow();
    }

    public TicketOptions BuildTicketOptions() => new()
    {
        Columns = Settings.GetInt(SettingKeys.PaperColumns, 48),
        Header = Settings.GetString(SettingKeys.TicketHeader, ""),
        Footer = Settings.GetString(SettingKeys.TicketFooter, "Obrigado!"),
        ShowPriceOnSenha = Settings.GetBool(SettingKeys.ShowPriceOnSenha, true),
        ShowRules = Settings.GetBool(SettingKeys.ShowRules, true),
        ShowDate = Settings.GetBool(SettingKeys.ShowDate, true),
        ShowTotalOnGroupSlip = Settings.GetBool(SettingKeys.ShowTotalOnGroupSlip, true),
        ShowPricesOnGroupSlip = Settings.GetBool(SettingKeys.ShowPricesOnGroupSlip, true),
        LineSpacingDots = Settings.GetInt(SettingKeys.LineSpacingDots, 0),
        FeedLinesBeforeCut = Settings.GetInt(SettingKeys.FeedLinesBeforeCut, 4),
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
            "serial" => new SerialTransport(target, SerialBaudRate),
            "windows" => new WindowsRawTransport(target),
            _ => new FileTransport(string.IsNullOrWhiteSpace(target) ? DefaultTicketFolder : target)
        };
    }

    public void Dispose()
    {
        Print.Dispose();
        Http.Dispose();
    }
}
