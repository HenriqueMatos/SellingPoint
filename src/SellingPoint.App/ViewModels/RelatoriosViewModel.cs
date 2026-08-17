using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App.ViewModels;

/// <summary>
/// A line in the report list: either a festival or one of its nights. One list of
/// two kinds rather than a tree, because a tree on a touch screen means a
/// disclosure arrow to hit, and there are only ever a handful of rows.
/// </summary>
public sealed class SessionRowViewModel
{
    private SessionRowViewModel(string name, string detail, bool isEvent)
    {
        Name = name;
        Detail = detail;
        IsEvent = isEvent;
    }

    public Event? Event { get; private init; }
    public Session? Session { get; private init; }

    public string Name { get; }
    public string Detail { get; }
    public bool IsEvent { get; }

    /// <summary>Nights sit under their festival. The vertical part is breathing room.</summary>
    public Thickness Indent => IsEvent ? new Thickness(0, 6, 0, 4) : new Thickness(20, 4, 0, 4);

    public FontWeight Weight => IsEvent ? FontWeight.Bold : FontWeight.SemiBold;

    public static SessionRowViewModel For(Event festival, int nights) =>
        new(festival.Name,
            nights == 1 ? "1 noite" : $"{nights} noites",
            isEvent: true)
        { Event = festival };

    public static SessionRowViewModel For(Session session) =>
        new(session.Name,
            session.IsOpen
                ? $"aberta {session.OpenedAt:dd/MM HH:mm} · a decorrer"
                : $"{session.OpenedAt:dd/MM HH:mm} — {session.ClosedAt:dd/MM HH:mm}",
            isEvent: false)
        { Session = session };
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
    private EventReport? _eventReport;

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

    // Taking a festival off a shared machine. Deliberately gated on having exported
    // it first: the automatic backups live in the same folder as the database and
    // would be deleted with it, so without this there would be no copy anywhere.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteEventLabel))]
    public partial bool DeleteArmed { get; set; }

    [ObservableProperty] public partial bool CanDeleteEvent { get; set; }

    public string DeleteEventLabel => "Apagar esta festa";

    /// <summary>
    /// What the export on disk actually covers.
    ///
    /// Not just which festival: how much of it. Export on Saturday, open Sunday
    /// under the same festival and take four hundred more sales, and an unlock
    /// keyed on the festival alone would still be good - and Sunday would be
    /// deleted having never been written to a file.
    /// </summary>
    private (int EventId, int Nights, int Sales) _exported;

    [ObservableProperty] public partial bool IsEventSelected { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial string UncountedText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseSessionLabel))]
    public partial bool CloseArmed { get; set; }

    public string CloseSessionLabel => CloseArmed ? "Confirmar o fecho" : "Fechar sessão";

    /// <summary>Changing the amount aims the button at a different number.</summary>
    partial void OnCountedEntryChanged(string value) => CloseArmed = false;

    // Sangria: cash carried out of the drawer mid-evening.
    [ObservableProperty] public partial string WithdrawalEntry { get; set; } = "";
    [ObservableProperty] public partial string WithdrawalReason { get; set; } = "";
    [ObservableProperty] public partial string MovementsText { get; set; } = "";
    [ObservableProperty] public partial bool HasMovements { get; set; }

    public ObservableCollection<AmountRowViewModel> MovementLines { get; } = [];

    /// <summary>
    /// Records money leaving the drawer. Only while the session is open: a closed
    /// session has already been counted, and moving its cash afterwards would
    /// change a figure somebody has signed off.
    /// </summary>
    [RelayCommand]
    private void RecordWithdrawal()
    {
        if (_report is null || !_report.Session.IsOpen)
        {
            StatusMessage = "Só se pode registar uma sangria com a sessão aberta.";
            return;
        }

        if (!Money.TryParseEuros(WithdrawalEntry, out var cents) || cents <= 0)
        {
            StatusMessage = "Escreva quanto saiu da caixa, por exemplo 200,00.";
            return;
        }

        services.Sales.RecordCashMovement(
            _report.Session.Id, -cents, WithdrawalReason.Trim(), DateTime.Now);

        var id = _report.Session.Id;
        WithdrawalEntry = "";
        WithdrawalReason = "";

        Load();
        SelectedSession = Sessions.FirstOrDefault(s => s.Session?.Id == id);

        StatusMessage = $"Registada a saída de {Money.Format(cents)}. "
                        + "O dinheiro esperado na caixa já desconta isso.";
    }

    public void Load()
    {
        var selectedSession = SelectedSession?.Session?.Id;
        var selectedEvent = SelectedSession?.Event?.Id;

        Sessions.Clear();

        var nights = services.Sales.GetSessions();
        foreach (var festival in services.Sales.GetEvents())
        {
            var itsNights = nights.Where(n => n.EventId == festival.Id).ToList();

            Sessions.Add(SessionRowViewModel.For(festival, itsNights.Count));
            foreach (var night in itsNights) Sessions.Add(SessionRowViewModel.For(night));
        }

        // Sessions from before festivals existed have no event to sit under. The
        // migration gives every one of them a festival, so this only catches a
        // database somebody edited by hand - but a report that silently drops a
        // night is worse than one with a loose row in it.
        foreach (var orphan in nights.Where(n => n.EventId is null))
            Sessions.Add(SessionRowViewModel.For(orphan));

        // Whatever was being looked at, then the night still running, then the top
        // of the list. The open night is the default because that is what anybody
        // opening this screen mid-festival came to do something about - count the
        // cash, close up, record what was carried to the car.
        SelectedSession =
            Sessions.FirstOrDefault(s => s.Session?.Id == selectedSession && selectedSession is not null)
            ?? Sessions.FirstOrDefault(s => s.Event?.Id == selectedEvent && selectedEvent is not null)
            ?? Sessions.FirstOrDefault(s => s.Session?.IsOpen == true)
            ?? Sessions.FirstOrDefault();
    }

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        ProductLines.Clear();
        CategoryLines.Clear();
        StockLines.Clear();
        MovementLines.Clear();

        _report = null;
        _eventReport = null;
        CloseArmed = false;
        DeleteArmed = false;

        // The export unlocks the festival it was taken of, at the size it was then.
        CanDeleteEvent = false;

        HasReport = value is not null;
        if (value is null)
        {
            IsSessionOpen = false;
            IsEventSelected = false;
            return;
        }

        if (value.Event is { } festival) ShowEvent(festival);
        else if (value.Session is { } session) ShowNight(session);

        // Checked after the report is built, against what the export covered. A
        // festival that has grown since needs exporting again.
        CanDeleteEvent = _eventReport is { } shown && Covered(shown) == _exported;
    }

    private static (int, int, int) Covered(EventReport report)
        => (report.Event.Id, report.Nights.Count, report.SalesCount);

    private void ShowNight(Session session)
    {
        var report = _report = _reports.Build(session);

        IsEventSelected = false;
        IsSessionOpen = session.IsOpen;
        CountedEntry = "";
        Title = session.Name;
        Subtitle = session.IsOpen ? "a decorrer" : "";

        Fill(report.SalesCount, report.CashCents, report.CardCents, report.TotalCents,
            session.OpeningFloatCents, report.ExpectedCashCents, report.CashMovements,
            report.CashMovementCents, report.Products, report.Categories);

        HasVariance = session.ClosingCountedCents is not null;
        CountedCashText = session.ClosingCountedCents is { } counted ? Money.Format(counted) : "—";
        VarianceText = Describe(report.VarianceCents);

        foreach (var stock in report.Stock) StockLines.Add(new StockRowViewModel(stock));
        HasStock = StockLines.Count > 0;
    }

    private void ShowEvent(Event festival)
    {
        var report = _eventReport =
            _reports.BuildForEvent(festival, services.Sales.GetSessions(festival.Id));

        IsEventSelected = true;
        IsSessionOpen = false;
        Title = festival.Name;
        Subtitle = report.Nights.Count == 1 ? "1 noite" : $"{report.Nights.Count} noites";

        Fill(report.SalesCount, report.CashCents, report.CardCents, report.TotalCents,
            report.FloatCents, report.ExpectedCashCents, report.CashMovements,
            report.CashMovementCents, report.Products, report.Categories);

        HasVariance = report.VarianceCents is not null;
        CountedCashText = Money.Format(report.CountedCashCents);
        VarianceText = Describe(report.VarianceCents);

        // Said plainly. A night nobody counted is money the difference above knows
        // nothing about, and the difference looking right is exactly when that
        // matters most.
        UncountedText = report.UncountedNights switch
        {
            0 => "",
            1 => "Há 1 noite por contar. A diferença não a inclui.",
            var n => $"Há {n} noites por contar. A diferença não as inclui."
        };

        HasStock = false;
    }

    private void Fill(int salesCount, int cash, int card, int total, int floatCents,
        int expected, IReadOnlyList<CashMovement> movements, int movementCents,
        IReadOnlyList<ProductSales> products, IReadOnlyList<CategorySales> categories)
    {
        SalesCountText = salesCount.ToString();
        CashText = Money.Format(cash);
        CardText = Money.Format(card);
        TotalText = Money.Format(total);
        FloatText = Money.Format(floatCents);
        ExpectedCashText = Money.Format(expected);

        HasMovements = movements.Count > 0;
        MovementsText = Money.Format(movementCents);
        foreach (var movement in movements)
        {
            MovementLines.Add(new AmountRowViewModel(
                string.IsNullOrWhiteSpace(movement.Reason) ? "Sangria" : movement.Reason,
                movement.CreatedAt.ToString("dd/MM HH:mm"), 0, movement.Cents));
        }

        foreach (var product in products)
            ProductLines.Add(new AmountRowViewModel(product.Name, product.CategoryName, product.Units, product.TotalCents));

        foreach (var category in categories)
            CategoryLines.Add(new AmountRowViewModel(category.Name, "", category.Units, category.TotalCents));
    }

    private static string Describe(int? variance) => variance switch
    {
        null => "—",
        0 => "certo",
        { } v => Money.Format(v)
    };

    [RelayCommand]
    private void CloseSession()
    {
        if (_report is null || !_report.Session.IsOpen) return;

        if (!Money.TryParseEuros(CountedEntry, out var counted))
        {
            StatusMessage = "Escreva o dinheiro contado, por exemplo 230,50.";
            return;
        }

        // There is no reopening a session - the repository has no such method, by
        // design. This button used to wear the same green as the harmless Guardar
        // on every other screen and sit a thumb's width from the box you type the
        // count into, so it asks first, and says the number it is about to close on.
        if (!CloseArmed)
        {
            CloseArmed = true;
            StatusMessage = $"Fechar a sessão com {Money.Format(counted)} contados. "
                            + "Não é possível reabrir. Toque outra vez para confirmar.";
            return;
        }

        CloseArmed = false;
        services.Sales.CloseSession(_report.Session.Id, counted, DateTime.Now);

        // A closing session is the natural moment to take a copy of the night.
        var backup = services.Db.Backup(DateTime.Now);

        var sessionId = _report.Session.Id;
        Load();
        SelectedSession = Sessions.FirstOrDefault(s => s.Session?.Id == sessionId);

        StatusMessage = $"Sessão fechada. Diferença: {VarianceText}. Cópia de segurança em {backup}";
    }

    [RelayCommand]
    private void ExportCsv() => StatusMessage = Export() is { } file
        ? $"Exportado para {file}"
        : "Escolha uma festa ou uma noite primeiro.";

    /// <summary>
    /// Writes whichever of the two is on screen and returns where it went. Split
    /// out from the command because deleting a festival has to export it first and
    /// needs to know the file was written.
    /// </summary>
    private string? Export()
    {
        var folder = Path.Combine(Path.GetDirectoryName(services.Db.Path) ?? ".", "relatórios");
        Directory.CreateDirectory(folder);

        string file;
        string contents;

        if (_eventReport is { } festival)
        {
            // The id is in the name because two festivals can otherwise collide:
            // "Festa" created twice on one day, or two in a weekend, would write to
            // the same path and the second would quietly replace the first.
            file = Path.Combine(folder,
                $"{Sanitise(festival.Event.Name)}-{festival.Event.CreatedAt:yyyyMMdd}-{festival.Event.Id}.csv");
            contents = ReportRepository.ToCsv(festival);
        }
        else if (_report is { } night)
        {
            file = Path.Combine(folder,
                $"{Sanitise(night.Session.Name)}-{night.Session.OpenedAt:yyyyMMdd-HHmm}.csv");
            contents = ReportRepository.ToCsv(night);
        }
        else return null;

        // Written beside and moved into place. WriteAllText truncates first, so a
        // disk that fills halfway through would leave the good export from an hour
        // ago as an empty file - and it is the only copy there is.
        var partial = file + ".part";
        File.WriteAllText(partial, contents, System.Text.Encoding.UTF8);
        File.Move(partial, file, overwrite: true);

        return file;
    }

    [RelayCommand]
    private void PrintSummary()
    {
        // A festival's summary is the same shape as a night's, so the printer sees
        // no difference; only what is written on it changes.
        var body = _eventReport is { } festival ? BuildPrintedSummary(festival)
                 : _report is { } night ? BuildPrintedSummary(night)
                 : null;

        if (body is null) return;

        services.Print.EnqueueText("FECHO DE CAIXA", body);
        StatusMessage = services.Print.Status.CanPrint
            ? "Resumo enviado para a impressora."
            : $"Resumo em espera: {services.Print.Status.Message}. Sai assim que a impressora responder.";
    }

    [RelayCommand]
    private void Backup() => StatusMessage = $"Cópia de segurança em {services.Db.Backup(DateTime.Now)}";

    /// <summary>
    /// Writes the festival out and takes a copy of the whole database, then lets
    /// the delete button work. Both, not one: the CSV is what a treasurer reads,
    /// the copy is what puts the sales back if somebody regrets this.
    /// </summary>
    [RelayCommand]
    private void ExportForDelete()
    {
        if (_eventReport is not { } festival) return;

        string file;
        string backup;

        try
        {
            if (Export() is not { } written) return;

            file = written;
            backup = services.Db.Backup(DateTime.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A full disk or a pulled stick must not leave the delete unlocked
            // pointing at a copy that was never finished.
            _exported = default;
            CanDeleteEvent = false;
            StatusMessage = $"Não consegui gravar a cópia: {e.Message}. Nada foi apagado.";
            return;
        }

        _exported = Covered(festival);
        CanDeleteEvent = true;
        DeleteArmed = false;

        StatusMessage = $"Guardado em {file} e em {backup}. Estes ficheiros estão neste "
                        + "mesmo computador — leve-os para uma pen ou para outro sítio "
                        + "antes de apagar, senão a única cópia vai com a máquina.";
    }

    /// <summary>
    /// Removes the festival from this machine. Products, prices and settings stay,
    /// so the next one does not begin by retyping forty products.
    /// </summary>
    /// <summary>
    /// Asks. Confirming is a different command on a different button, which only
    /// appears once this has been pressed - unlike the two-tap confirms elsewhere
    /// in the app, where arming and confirming share one button. Those cost a
    /// product or a queue; a double tap on a wet screen here costs a festival, and
    /// two contacts at one place milliseconds apart is exactly what a wet screen
    /// produces.
    /// </summary>
    [RelayCommand]
    private void DeleteEvent()
    {
        if (_eventReport is not { } report) return;

        if (!CanDeleteEvent)
        {
            StatusMessage = _exported.EventId == report.Event.Id
                ? "A festa cresceu desde a última cópia. Exporte outra vez antes de apagar."
                : "Exporte a festa primeiro. Depois de apagada não há por onde a ir buscar.";
            return;
        }

        DeleteArmed = true;

        // Said before, not discovered after: slips still queued belong to people who
        // paid and never got them, and deleting the festival takes them too.
        var waiting = services.Print.PendingCount > 0
            ? $" Há {services.Print.PendingCount} talão(ões) ainda por imprimir que se perdem com ela."
            : "";

        StatusMessage = $"Apagar «{report.Event.Name}» tira desta máquina as suas "
                        + $"{report.Nights.Count} noite(s), {report.SalesCount} venda(s) e "
                        + $"{Money.Format(report.TotalCents)} de receita registada."
                        + waiting
                        + " Os produtos e os preços ficam. Isto não se desfaz.";
    }

    [RelayCommand]
    private void CancelDeleteEvent()
    {
        DeleteArmed = false;
        StatusMessage = "Nada foi apagado.";
    }

    [RelayCommand]
    private void ConfirmDeleteEvent()
    {
        if (_eventReport is not { } report || !DeleteArmed || !CanDeleteEvent) return;

        var name = report.Event.Name;

        try
        {
            services.Sales.DeleteEvent(report.Event.Id);
        }
        catch (InvalidOperationException e)
        {
            DeleteArmed = false;
            StatusMessage = e.Message;
            return;
        }

        DeleteArmed = false;
        CanDeleteEvent = false;
        _exported = default;

        Load();
        StatusMessage = $"«{name}» apagada desta máquina. Os produtos e os preços ficaram. {LeftBehind()}";
    }

    /// <summary>
    /// What is still on the disk after the delete, said plainly.
    ///
    /// Every automatic copy is a snapshot of the whole database, so one taken while
    /// the festival was running still holds all of it. They are not deleted here:
    /// they are the only way back if this was a mistake, and throwing them away
    /// would make the export gate theatre. But leaving somebody to believe the
    /// festival is off a shared computer when it is sitting in the next folder
    /// along is worse than either.
    /// </summary>
    private string LeftBehind()
    {
        var folder = Path.GetDirectoryName(services.Db.Path);
        if (folder is null) return "";

        var copies = Directory.GetFiles(folder, "backup-*.db").Length;
        if (copies == 0) return "";

        return $"Atenção: ficam em {folder} {copies} cópia(s) de segurança que ainda contêm esta festa. "
               + "Leve-as consigo ou apague-as se quiser mesmo que ela desapareça deste computador.";
    }

    /// <summary>The whole festival on one slip, with a line per night.</summary>
    private List<string> BuildPrintedSummary(EventReport report)
    {
        var width = services.Printer.Options.Columns;
        var lines = new List<string>
        {
            report.Event.Name,
            $"{report.Nights.Count} noite(s)",
            Layout.Rule('-', width),
            Layout.LeftRight("Vendas", report.SalesCount.ToString(), width),
            Layout.LeftRight("Dinheiro", Money.Format(report.CashCents), width),
            Layout.LeftRight("Cartão", Money.Format(report.CardCents), width),
            Layout.LeftRight("TOTAL", Money.Format(report.TotalCents), width),
            Layout.Rule('-', width)
        };

        foreach (var night in report.Nights)
        {
            lines.Add(Layout.LeftRight(
                $"{night.Session.OpenedAt:dd/MM} {night.Session.Name}",
                Money.Format(night.TotalCents), width));
        }

        lines.Add(Layout.Rule('-', width));
        lines.Add(Layout.LeftRight("Fundos de caixa", Money.Format(report.FloatCents), width));

        if (report.CashMovementCents != 0)
            lines.Add(Layout.LeftRight("Sangrias", Money.Format(report.CashMovementCents), width));

        lines.Add(Layout.LeftRight("Dinheiro esperado", Money.Format(report.ExpectedCashCents), width));
        lines.Add(Layout.LeftRight("Dinheiro contado", Money.Format(report.CountedCashCents), width));

        if (report.VarianceCents is { } variance)
            lines.Add(Layout.LeftRight("Diferença", Money.Format(variance), width));

        // On paper as well as on screen: a night nobody counted is money this
        // difference knows nothing about.
        if (report.UncountedNights > 0)
            lines.Add(Layout.LeftRight("Noites por contar", report.UncountedNights.ToString(), width));

        lines.Add(Layout.Rule('-', width));
        lines.Add("POR PRODUTO");
        lines.AddRange(report.Products.Select(p =>
            Layout.LeftRight($"{p.Units}x {p.Name}", Money.Format(p.TotalCents), width)));

        return lines;
    }

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
            Layout.LeftRight("Fundo de caixa", Money.Format(report.Session.OpeningFloatCents), width)
        };

        // Listed one by one, not just totalled: at two in the morning the question
        // is not how much left the drawer but who carried it and when.
        foreach (var movement in report.CashMovements)
        {
            var label = string.IsNullOrWhiteSpace(movement.Reason)
                ? $"{movement.CreatedAt:HH:mm} sangria"
                : $"{movement.CreatedAt:HH:mm} {movement.Reason}";

            lines.Add(Layout.LeftRight(label, Money.Format(movement.Cents), width));
        }

        lines.Add(Layout.LeftRight("Dinheiro esperado", Money.Format(report.ExpectedCashCents), width));

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
