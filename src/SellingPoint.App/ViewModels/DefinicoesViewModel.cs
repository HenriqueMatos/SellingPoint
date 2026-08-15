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

    public ObservableCollection<Choice<PaperWidth>> Papers { get; } =
    [
        new("80 mm (rolo largo)", PaperWidth.Wide),
        new("58 mm (rolo estreito)", PaperWidth.Narrow)
    ];

    // The column count is not offered: it follows from these two, because on a
    // thermal printer the letter size and the characters per line are one thing.
    public ObservableCollection<Choice<TicketFontSize>> FontSizes { get; } =
    [
        new("Pequena — cabe mais texto", TicketFontSize.Small),
        new("Normal", TicketFontSize.Normal),
        new("Grande — metade dos caracteres por linha", TicketFontSize.Large)
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
    [ObservableProperty] public partial Choice<PaperWidth>? Paper { get; set; }
    [ObservableProperty] public partial Choice<TicketFontSize>? FontSize { get; set; }
    [ObservableProperty] public partial string FormatText { get; set; } = "";
    [ObservableProperty] public partial string TicketPreview { get; set; } = "";
    [ObservableProperty] public partial bool NamesGetCut { get; set; }
    [ObservableProperty] public partial string CutExample { get; set; } = "";
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

    // --- updates -----------------------------------------------------------
    [ObservableProperty] public partial string VersionText { get; set; } = "";
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    [ObservableProperty] public partial string UpdateNotes { get; set; } = "";
    [ObservableProperty] public partial bool UpdateAvailable { get; set; }
    [ObservableProperty] public partial bool UpdateBlockedBySession { get; set; }
    [ObservableProperty] public partial bool UpdateReady { get; set; }
    [ObservableProperty] public partial bool IsBusyWithUpdate { get; set; }

    private ReleaseInfo? _release;
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    public void Load()
    {
        var settings = services.Settings;

        Transport = Pick(Transports, settings.GetString(SettingKeys.PrinterTransport, "file"));
        Target = settings.GetString(SettingKeys.PrinterTarget, "");
        Paper = Pick(Papers, services.PaperWidthSetting);
        FontSize = Pick(FontSizes, services.FontSizeSetting);
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
        VersionText = $"Versão {UpdateChecker.Current}";
        UpdateReady = services.Installer.HasPendingUpdate;
        if (UpdateReady) UpdateStatus = "Atualização descarregada. Fecha e volta a abrir para ficar aplicada.";

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
    partial void OnPaperChanged(Choice<PaperWidth>? value) => RefreshPaperCost();
    partial void OnFontSizeChanged(Choice<TicketFontSize>? value) => RefreshPaperCost();
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
            Paper = Paper?.Value ?? PaperWidth.Wide,
            FontSize = FontSize?.Value ?? TicketFontSize.Normal,
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
        FormatText = PaperFormat.Describe(options.Paper, options.FontSize);

        RefreshPreview(options);
    }

    /// <summary>
    /// Draws the real slip at the real width, so the cut is seen here rather than
    /// discovered on the paper. The sample name is deliberately a long one.
    /// </summary>
    private void RefreshPreview(TicketOptions options)
    {
        var sale = new Sale
        {
            TicketNumber = 42,
            CreatedAt = DateTime.Now,
            TotalCents = 750,
            Lines =
            [
                new SaleLine { ProductName = "Sandes de Leitão", Qty = 1, UnitPriceCents = 400,
                               LineTotalCents = 400, PrintGroup = "Cozinha", SlipMode = SlipMode.Grouped,
                               CategoryName = "Comida" },
                new SaleLine { ProductName = "Cerveja", Qty = 2, UnitPriceCents = 150,
                               LineTotalCents = 300, PrintGroup = "Bar", SlipMode = SlipMode.PerUnit,
                               CategoryName = "Bebidas" }
            ]
        };

        var slips = TicketBuilder.Build(sale, options);
        TicketPreview = SlipPreview.ToText(slips, options);

        // Warn only when a name is genuinely losing letters, not merely because
        // the line is full.
        const string longest = "1x Sandes de Leitão";
        var room = options.Columns - Money.Format(400).Length - 1;

        NamesGetCut = room < longest.Length;
        CutExample = NamesGetCut
            ? $"«{longest}» fica «{Layout.Truncate(longest, Math.Max(room, 0))}»"
            : "";
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
        settings.Set(SettingKeys.PaperWidth, Paper?.Value ?? PaperWidth.Wide);
        settings.Set(SettingKeys.TicketFontSize, FontSize?.Value ?? TicketFontSize.Normal);
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

    /// <summary>
    /// Asks GitHub what the latest version is. Only ever reports - a till must not
    /// update itself with a queue at the counter.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdate()
    {
        IsBusyWithUpdate = true;
        UpdateStatus = "A procurar...";
        UpdateAvailable = false;
        UpdateNotes = "";

        try
        {
            _release = await services.Updates.LatestAsync();

            if (_release is null)
            {
                UpdateStatus = "Não foi possível saber. Verifique a ligação à internet.";
                return;
            }

            if (!UpdateChecker.IsNewer(_release.Version, UpdateChecker.Current))
            {
                UpdateStatus = "Já tem a versão mais recente.";
                return;
            }

            UpdateAvailable = true;
            UpdateNotes = _release.Notes;
            UpdateStatus = $"Há a versão {_release.Version} ({_release.SizeText}).";

            // Downloading is harmless mid-event; the swap is what waits.
            UpdateBlockedBySession = services.Sales.GetOpenSession() is not null;
        }
        catch (Exception e)
        {
            UpdateStatus = $"Não foi possível procurar: {e.Message}";
        }
        finally
        {
            IsBusyWithUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (_release is null) return;

        IsBusyWithUpdate = true;
        UpdateStatus = "A descarregar...";

        try
        {
            await services.Installer.DownloadAsync(services.Http, _release);

            UpdateReady = true;
            UpdateAvailable = false;
            UpdateStatus = "Descarregada. Fecha e volta a abrir para ficar aplicada.";
        }
        catch (Exception e)
        {
            services.Installer.DiscardPending();
            UpdateStatus = $"A descarga falhou: {e.Message}";
        }
        finally
        {
            IsBusyWithUpdate = false;
        }
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        services.Installer.DiscardPending();
        UpdateReady = false;
        UpdateStatus = "Atualização descartada. Continua na versão atual.";
    }

    [RelayCommand]
    private void Backup() => StatusMessage = $"Cópia de segurança em {services.Db.Backup(DateTime.Now)}";

    private static Choice<T>? Pick<T>(IEnumerable<Choice<T>> choices, T value)
        => choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value));
}
