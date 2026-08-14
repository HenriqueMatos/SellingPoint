using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App.ViewModels;

public sealed class SessionRowViewModel(Session session)
{
    public Session Session { get; } = session;
    public string Name => Session.Name;
    public string Detail => Session.IsOpen
        ? $"aberta {Session.OpenedAt:dd/MM HH:mm} · a decorrer"
        : $"{Session.OpenedAt:dd/MM HH:mm} — {Session.ClosedAt:dd/MM HH:mm}";
}

public sealed class AmountRowViewModel(string name, string detail, int units, int cents)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public string UnitsText { get; } = units.ToString();
    public string TotalText { get; } = Money.Format(cents);
}

public sealed class StockRowViewModel(StockLine line)
{
    public string Name => line.Name;
    public string SoldText => line.Sold.ToString();
    public string AdjustedText => line.Adjusted == 0 ? "—" : $"{line.Adjusted:+#;-#;0}";
    public string RemainingText => line.Remaining.ToString();
}

public partial class RelatoriosViewModel(AppServices services) : ViewModelBase
{
    private readonly ReportRepository _reports = new(services.Db);
    private SessionReport? _report;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];
    public ObservableCollection<AmountRowViewModel> ProductLines { get; } = [];
    public ObservableCollection<AmountRowViewModel> CategoryLines { get; } = [];
    public ObservableCollection<StockRowViewModel> StockLines { get; } = [];

    [ObservableProperty] public partial SessionRowViewModel? SelectedSession { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    [ObservableProperty] public partial string SalesCountText { get; set; } = "0";
    [ObservableProperty] public partial string CashText { get; set; } = "";
    [ObservableProperty] public partial string CardText { get; set; } = "";
    [ObservableProperty] public partial string TotalText { get; set; } = "";
    [ObservableProperty] public partial string FloatText { get; set; } = "";
    [ObservableProperty] public partial string ExpectedCashText { get; set; } = "";
    [ObservableProperty] public partial string CountedCashText { get; set; } = "";
    [ObservableProperty] public partial string VarianceText { get; set; } = "";
    [ObservableProperty] public partial bool HasVariance { get; set; }
    [ObservableProperty] public partial bool HasReport { get; set; }
    [ObservableProperty] public partial bool HasStock { get; set; }

    [ObservableProperty] public partial bool IsSessionOpen { get; set; }
    [ObservableProperty] public partial string CountedEntry { get; set; } = "";

    public void Load()
    {
        var selectedId = SelectedSession?.Session.Id;

        Sessions.Clear();
        foreach (var session in services.Sales.GetSessions()) Sessions.Add(new SessionRowViewModel(session));

        SelectedSession = Sessions.FirstOrDefault(s => s.Session.Id == selectedId) ?? Sessions.FirstOrDefault();
    }

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        ProductLines.Clear();
        CategoryLines.Clear();
        StockLines.Clear();

        HasReport = value is not null;
        if (value is null)
        {
            _report = null;
            IsSessionOpen = false;
            return;
        }

        var report = _report = _reports.Build(value.Session);
        IsSessionOpen = value.Session.IsOpen;
        CountedEntry = "";

        SalesCountText = report.SalesCount.ToString();
        CashText = Money.Format(report.CashCents);
        CardText = Money.Format(report.CardCents);
        TotalText = Money.Format(report.TotalCents);
        FloatText = Money.Format(report.Session.OpeningFloatCents);
        ExpectedCashText = Money.Format(report.ExpectedCashCents);

        HasVariance = report.Session.ClosingCountedCents is not null;
        CountedCashText = report.Session.ClosingCountedCents is { } counted ? Money.Format(counted) : "—";
        VarianceText = report.VarianceCents is { } variance
            ? (variance == 0 ? "certo" : Money.Format(variance))
            : "—";

        foreach (var product in report.Products)
            ProductLines.Add(new AmountRowViewModel(product.Name, product.CategoryName, product.Units, product.TotalCents));

        foreach (var category in report.Categories)
            CategoryLines.Add(new AmountRowViewModel(category.Name, "", category.Units, category.TotalCents));

        foreach (var stock in report.Stock) StockLines.Add(new StockRowViewModel(stock));
        HasStock = StockLines.Count > 0;
    }

    [RelayCommand]
    private void CloseSession()
    {
        if (_report is null || !_report.Session.IsOpen) return;

        if (!Money.TryParseEuros(CountedEntry, out var counted))
        {
            StatusMessage = "Escreva o dinheiro contado, por exemplo 230,50.";
            return;
        }

        services.Sales.CloseSession(_report.Session.Id, counted, DateTime.Now);

        // A closing session is the natural moment to take a copy of the night.
        var backup = services.Db.Backup(DateTime.Now);

        var sessionId = _report.Session.Id;
        Load();
        SelectedSession = Sessions.FirstOrDefault(s => s.Session.Id == sessionId);

        StatusMessage = $"Sessão fechada. Diferença: {VarianceText}. Cópia de segurança em {backup}";
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (_report is null) return;

        var folder = Path.Combine(Path.GetDirectoryName(services.Db.Path) ?? ".", "relatórios");
        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, $"{Sanitise(_report.Session.Name)}-{_report.Session.OpenedAt:yyyyMMdd-HHmm}.csv");
        File.WriteAllText(file, ReportRepository.ToCsv(_report), System.Text.Encoding.UTF8);

        StatusMessage = $"Exportado para {file}";
    }

    [RelayCommand]
    private void PrintSummary()
    {
        if (_report is null) return;

        services.Print.EnqueueText("FECHO DE CAIXA", BuildPrintedSummary(_report));
        StatusMessage = services.Print.Status.CanPrint
            ? "Resumo enviado para a impressora."
            : $"Resumo em espera: {services.Print.Status.Message}. Sai assim que a impressora responder.";
    }

    [RelayCommand]
    private void Backup() => StatusMessage = $"Cópia de segurança em {services.Db.Backup(DateTime.Now)}";

    private List<string> BuildPrintedSummary(SessionReport report)
    {
        var width = services.Printer.Options.Columns;
        var lines = new List<string>
        {
            report.Session.Name,
            $"Aberta   {report.Session.OpenedAt:dd/MM/yyyy HH:mm}",
            report.Session.ClosedAt is { } closed ? $"Fechada  {closed:dd/MM/yyyy HH:mm}" : "Ainda aberta",
            Layout.Rule('-', width),
            Layout.LeftRight("Vendas", report.SalesCount.ToString(), width),
            Layout.LeftRight("Dinheiro", Money.Format(report.CashCents), width),
            Layout.LeftRight("Cartão", Money.Format(report.CardCents), width),
            Layout.LeftRight("TOTAL", Money.Format(report.TotalCents), width),
            Layout.Rule('-', width),
            Layout.LeftRight("Fundo de caixa", Money.Format(report.Session.OpeningFloatCents), width),
            Layout.LeftRight("Dinheiro esperado", Money.Format(report.ExpectedCashCents), width)
        };

        if (report.Session.ClosingCountedCents is { } counted)
        {
            lines.Add(Layout.LeftRight("Dinheiro contado", Money.Format(counted), width));
            lines.Add(Layout.LeftRight("Diferença", Money.Format(report.VarianceCents ?? 0), width));
        }

        lines.Add(Layout.Rule('-', width));
        lines.Add("POR PRODUTO");
        lines.AddRange(report.Products.Select(p =>
            Layout.LeftRight($"{p.Units}x {p.Name}", Money.Format(p.TotalCents), width)));

        if (report.Stock.Count > 0)
        {
            lines.Add(Layout.Rule('-', width));
            lines.Add("STOCK");
            lines.AddRange(report.Stock.Select(s =>
                Layout.LeftRight(s.Name, $"{s.Remaining}", width)));
        }

        return lines;
    }

    private static string Sanitise(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
}
