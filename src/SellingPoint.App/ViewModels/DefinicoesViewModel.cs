using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Data;

namespace SellingPoint.App.ViewModels;

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public partial class DefinicoesViewModel(AppServices services) : ViewModelBase
{
    public ObservableCollection<Choice<string>> Transports { get; } =
    [
        new("Ficheiros (sem impressora — para testar)", "file"),
        new("Rede / WiFi (porta 9100)", "network"),
        new("Porta série / USB-COM", "serial"),
        new("Impressora do Windows", "windows")
    ];

    public ObservableCollection<Choice<int>> PaperWidths { get; } =
    [
        new("80 mm — 48 colunas", 48),
        new("58 mm — 32 colunas", 32)
    ];

    public ObservableCollection<Choice<int>> CodePages { get; } =
    [
        new("858 — acentos e sinal de euro (recomendado)", 858),
        new("860 — português, sem sinal de euro", 860),
        new("1252 — Windows ocidental", 1252),
        new("437 — apenas ASCII", 437)
    ];

    public ObservableCollection<Choice<OutOfStockBehaviour>> StockBehaviours { get; } =
    [
        new("Avisar e vender à mesma", OutOfStockBehaviour.Warn),
        new("Impedir a venda", OutOfStockBehaviour.Block)
    ];

    [ObservableProperty] public partial Choice<string>? Transport { get; set; }
    [ObservableProperty] public partial string Target { get; set; } = "";
    [ObservableProperty] public partial string TargetHint { get; set; } = "";
    [ObservableProperty] public partial Choice<int>? PaperWidth { get; set; }
    [ObservableProperty] public partial Choice<int>? CodePage { get; set; }
    [ObservableProperty] public partial Choice<OutOfStockBehaviour>? StockBehaviour { get; set; }

    [ObservableProperty] public partial string Header { get; set; } = "";
    [ObservableProperty] public partial string Footer { get; set; } = "";
    [ObservableProperty] public partial bool ShowPriceOnSenha { get; set; } = true;
    [ObservableProperty] public partial bool PrintSummarySlip { get; set; }
    [ObservableProperty] public partial bool FoldAccents { get; set; }
    [ObservableProperty] public partial bool OpenCashDrawer { get; set; }

    [ObservableProperty] public partial string DatabasePath { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    public void Load()
    {
        var settings = services.Settings;

        Transport = Pick(Transports, settings.GetString(SettingKeys.PrinterTransport, "file"));
        Target = settings.GetString(SettingKeys.PrinterTarget, "");
        PaperWidth = Pick(PaperWidths, settings.GetInt(SettingKeys.PaperColumns, 48));
        CodePage = Pick(CodePages, settings.GetInt(SettingKeys.CodePage, 858));
        StockBehaviour = Pick(StockBehaviours, services.OutOfStock);

        Header = settings.GetString(SettingKeys.TicketHeader, "");
        Footer = settings.GetString(SettingKeys.TicketFooter, "Obrigado!");
        ShowPriceOnSenha = settings.GetBool(SettingKeys.ShowPriceOnSenha, true);
        PrintSummarySlip = settings.GetBool(SettingKeys.PrintSummarySlip, false);
        FoldAccents = settings.GetBool(SettingKeys.FoldAccents, false);
        OpenCashDrawer = settings.GetBool(SettingKeys.OpenCashDrawer, false);

        DatabasePath = services.Db.Path;
        StatusMessage = $"Impressora atual: {services.Printer.Transport.Describe()}";
    }

    partial void OnTransportChanged(Choice<string>? value) => TargetHint = value?.Value switch
    {
        "network" => "Endereço IP, por exemplo 192.168.1.50 ou 192.168.1.50:9100",
        "serial" => "Nome da porta, por exemplo COM3",
        "windows" => "Nome exato da impressora como aparece no Windows",
        _ => $"Pasta onde gravar os talões. Vazio usa {services.DefaultTicketFolder}"
    };

    [RelayCommand]
    private void Save()
    {
        var settings = services.Settings;

        settings.Set(SettingKeys.PrinterTransport, Transport?.Value ?? "file");
        settings.Set(SettingKeys.PrinterTarget, Target.Trim());
        settings.Set(SettingKeys.PaperColumns, PaperWidth?.Value ?? 48);
        settings.Set(SettingKeys.CodePage, CodePage?.Value ?? 858);
        settings.Set(SettingKeys.OutOfStockBehaviour, StockBehaviour?.Value ?? OutOfStockBehaviour.Warn);

        settings.Set(SettingKeys.TicketHeader, Header.Trim());
        settings.Set(SettingKeys.TicketFooter, Footer.Trim());
        settings.Set(SettingKeys.ShowPriceOnSenha, ShowPriceOnSenha);
        settings.Set(SettingKeys.PrintSummarySlip, PrintSummarySlip);
        settings.Set(SettingKeys.FoldAccents, FoldAccents);
        settings.Set(SettingKeys.OpenCashDrawer, OpenCashDrawer);

        services.ReloadPrinter();
        StatusMessage = $"Definições guardadas. Impressora: {services.Printer.Transport.Describe()}";
    }

    /// <summary>
    /// The one button that answers "is this printer set up correctly". The accent
    /// line on the printout is the real test: if those come out as line-drawing
    /// characters, the code page is wrong.
    /// </summary>
    [RelayCommand]
    private async Task TestPrint()
    {
        Save();

        try
        {
            await Task.Run(services.Printer.PrintTest);
            StatusMessage = $"Teste enviado para {services.Printer.Transport.Describe()}.";
        }
        catch (Exception e)
        {
            StatusMessage = $"Falha no teste: {e.Message}";
        }
    }

    [RelayCommand]
    private void Backup() => StatusMessage = $"Cópia de segurança em {services.Db.Backup(DateTime.Now)}";

    private static Choice<T>? Pick<T>(IEnumerable<Choice<T>> choices, T value)
        => choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value));
}
