using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

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

    /// <summary>What the machine actually has: Windows printers, or COM ports.</summary>
    public ObservableCollection<string> AvailableTargets { get; } = [];

    [ObservableProperty] public partial Choice<string>? Transport { get; set; }
    [ObservableProperty] public partial string Target { get; set; } = "";
    [ObservableProperty] public partial bool IsSerial { get; set; }
    [ObservableProperty] public partial string? SelectedTarget { get; set; }
    [ObservableProperty] public partial bool ShowTargetList { get; set; }
    [ObservableProperty] public partial string TargetListHint { get; set; } = "";
    [ObservableProperty] public partial string TargetHint { get; set; } = "";
    [ObservableProperty] public partial Choice<int>? PaperWidth { get; set; }
    [ObservableProperty] public partial Choice<int>? CodePage { get; set; }
    [ObservableProperty] public partial Choice<OutOfStockBehaviour>? StockBehaviour { get; set; }

    [ObservableProperty] public partial string Header { get; set; } = "";
    [ObservableProperty] public partial string Footer { get; set; } = "";
    [ObservableProperty] public partial bool ShowPriceOnSenha { get; set; } = true;

    // --- paper -------------------------------------------------------------
    [ObservableProperty] public partial bool ShowRules { get; set; } = true;
    [ObservableProperty] public partial bool ShowDate { get; set; } = true;
    [ObservableProperty] public partial bool ShowTotalOnGroupSlip { get; set; } = true;
    [ObservableProperty] public partial bool ShowPricesOnGroupSlip { get; set; } = true;
    [ObservableProperty] public partial string LineSpacing { get; set; } = "30";
    [ObservableProperty] public partial string FeedLines { get; set; } = "4";

    [ObservableProperty] public partial string PaperCostText { get; set; } = "";
    [ObservableProperty] public partial bool FeedTooShort { get; set; }
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
        ShowRules = settings.GetBool(SettingKeys.ShowRules, true);
        ShowDate = settings.GetBool(SettingKeys.ShowDate, true);
        ShowTotalOnGroupSlip = settings.GetBool(SettingKeys.ShowTotalOnGroupSlip, true);
        ShowPricesOnGroupSlip = settings.GetBool(SettingKeys.ShowPricesOnGroupSlip, true);
        LineSpacing = settings.GetInt(SettingKeys.LineSpacingDots, 0) is var dots && dots > 0 ? dots.ToString() : "30";
        FeedLines = settings.GetInt(SettingKeys.FeedLinesBeforeCut, 4).ToString();
        PrintSummarySlip = settings.GetBool(SettingKeys.PrintSummarySlip, false);
        FoldAccents = settings.GetBool(SettingKeys.FoldAccents, false);
        OpenCashDrawer = settings.GetBool(SettingKeys.OpenCashDrawer, false);

        DatabasePath = services.Db.Path;
        RefreshTargets();
        RefreshPaperCost();
        StatusMessage = $"Impressora atual: {services.Printer.Transport.Describe()}";
    }

    partial void OnTransportChanged(Choice<string>? value)
    {
        IsSerial = value?.Value == "serial";
        TargetHint = Hint(value?.Value);
        RefreshTargets();
    }

    partial void OnShowRulesChanged(bool value) => RefreshPaperCost();
    partial void OnShowDateChanged(bool value) => RefreshPaperCost();
    partial void OnShowTotalOnGroupSlipChanged(bool value) => RefreshPaperCost();
    partial void OnShowPricesOnGroupSlipChanged(bool value) => RefreshPaperCost();
    partial void OnShowPriceOnSenhaChanged(bool value) => RefreshPaperCost();
    partial void OnLineSpacingChanged(string value) => RefreshPaperCost();
    partial void OnFeedLinesChanged(string value) => RefreshPaperCost();
    partial void OnPaperWidthChanged(Choice<int>? value) => RefreshPaperCost();
    partial void OnHeaderChanged(string value) => RefreshPaperCost();
    partial void OnFooterChanged(string value) => RefreshPaperCost();

    private int ParsedSpacing => int.TryParse(LineSpacing, out var v) && v is > 0 and <= 255 ? v : 0;
    private int ParsedFeed => int.TryParse(FeedLines, out var v) && v is >= 0 and <= 20 ? v : 4;

    /// <summary>
    /// Measures a real slip rather than estimating from a formula, so the number
    /// on screen cannot drift away from what actually prints.
    /// </summary>
    private void RefreshPaperCost()
    {
        var options = new TicketOptions
        {
            Columns = PaperWidth?.Value ?? 48,
            Header = Header,
            Footer = Footer,
            ShowPriceOnSenha = ShowPriceOnSenha,
            ShowRules = ShowRules,
            ShowDate = ShowDate,
            ShowTotalOnGroupSlip = ShowTotalOnGroupSlip,
            ShowPricesOnGroupSlip = ShowPricesOnGroupSlip,
            LineSpacingDots = ParsedSpacing,
            FeedLinesBeforeCut = ParsedFeed
        };

        var group = PaperEstimate.ForGroupSlip(options);
        var senha = PaperEstimate.ForSenha(options);

        PaperCostText = $"Talão de 2 artigos: {group}.  Senha: {senha}.";
        FeedTooShort = ParsedFeed < 3;
    }

    /// <summary>Tapping a name in the list is the same as typing it, without the typos.</summary>
    partial void OnSelectedTargetChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Target = value;
    }

    /// <summary>
    /// Fills the list the moment a connection is chosen, rather than waiting for a
    /// button. Both are instant: naming what exists is cheap, and only working out
    /// which COM port answers like a printer is slow enough to need Procurar.
    /// </summary>
    private void RefreshTargets()
    {
        var transport = Transport?.Value;

        AvailableTargets.Clear();
        foreach (var target in transport switch
                 {
                     "windows" => WindowsPrinters.List(),
                     "serial" => PrinterLocator.AvailablePorts(),
                     _ => []
                 })
        {
            AvailableTargets.Add(target);
        }

        ShowTargetList = transport is "windows" or "serial";
        SelectedTarget = AvailableTargets.FirstOrDefault(t => t == Target);

        TargetListHint = (transport, AvailableTargets.Count) switch
        {
            ("windows", 0) => "O Windows não tem nenhuma impressora instalada.",
            ("windows", _) => "Toque na impressora que quer usar.",
            ("serial", 0) => "Não há portas série nesta máquina.",
            ("serial", _) => "Toque numa porta, ou use «Procurar» para descobrir qual responde.",
            _ => ""
        };
    }

    private string Hint(string? transport) => transport switch
    {
        "network" => "Endereço IP, por exemplo 192.168.1.50 ou 192.168.1.50:9100",
        "serial" => "Nome da porta, por exemplo COM3",
        "windows" => "Escolha da lista abaixo. Nesta ligação não é possível consultar o estado "
                     + "(sem papel, tampa aberta) — só imprimir.",
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
        settings.Set(SettingKeys.ShowRules, ShowRules);
        settings.Set(SettingKeys.ShowDate, ShowDate);
        settings.Set(SettingKeys.ShowTotalOnGroupSlip, ShowTotalOnGroupSlip);
        settings.Set(SettingKeys.ShowPricesOnGroupSlip, ShowPricesOnGroupSlip);
        settings.Set(SettingKeys.LineSpacingDots, ParsedSpacing);
        settings.Set(SettingKeys.FeedLinesBeforeCut, ParsedFeed);
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

        var status = await Task.Run(() => services.Print.CheckStatus());
        services.Print.EnqueueTest();

        StatusMessage = status.CanPrint
            ? $"Teste enviado para {services.Printer.Transport.Describe()}. Confirme a linha dos acentos."
            : $"{status.Message}. O teste fica em espera e sai assim que a impressora responder.";
    }

    /// <summary>Lists the COM ports on the machine so the right one can be chosen
    /// without going into the Gestor de Dispositivos.</summary>
    [RelayCommand]
    private async Task ScanPorts()
    {
        StatusMessage = "A procurar portas...";
        var probes = await Task.Run(() => PrinterLocator.ScanAll(services.SerialBaudRate));

        AvailableTargets.Clear();
        foreach (var probe in probes) AvailableTargets.Add(probe.PortName);

        var printer = probes.FirstOrDefault(p => p.AnsweredAsPrinter);
        if (printer is not null)
        {
            Target = printer.PortName;
            StatusMessage = $"Impressora encontrada em {printer.PortName}. Carregue em Guardar.";
            return;
        }

        StatusMessage = probes.Count == 0
            ? "Não há portas série nesta máquina. Se a impressora está ligada por USB, veja em "
              + "Definições do Windows → Impressoras e scanners e use a ligação «Impressora do Windows»."
            : PrinterDiagnosticsViewModel.NoPrinterAdvice(probes);
    }

    [RelayCommand]
    private void Backup() => StatusMessage = $"Cópia de segurança em {services.Db.Backup(DateTime.Now)}";

    private static Choice<T>? Pick<T>(IEnumerable<Choice<T>> choices, T value)
        => choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value));
}
