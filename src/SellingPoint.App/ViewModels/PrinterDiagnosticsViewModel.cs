using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App.ViewModels;

public sealed class QueuedJobViewModel(PrintJob job)
{
    public string Title => job.Title;
    public string Detail => job.Attempts == 0
        ? $"em espera desde {job.CreatedAt:HH:mm}"
        : $"{job.Attempts} tentativa(s) · {job.LastError}";
}

public sealed class PortProbeViewModel(PortProbe probe)
{
    public string PortName => probe.PortName;
    public string Message => probe.Message;
    public bool IsPrinter => probe.AnsweredAsPrinter;
    public IBrush Marker => probe.AnsweredAsPrinter ? Brushes.MediumSeaGreen : Brushes.Gray;
}

/// <summary>
/// The screen for when the printer stops mid-event. Says what is wrong, what is
/// waiting, and which COM ports have something on them - the questions that
/// otherwise mean a trip into Device Manager with a queue forming.
/// </summary>
public partial class PrinterDiagnosticsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public PrinterDiagnosticsViewModel(AppServices services)
    {
        _services = services;
        _services.Print.Changed += OnPrintServiceChanged;
        Refresh();
    }

    public ObservableCollection<QueuedJobViewModel> Queue { get; } = [];
    public ObservableCollection<PortProbeViewModel> Ports { get; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = Brushes.Gray;
    [ObservableProperty] public partial string AdviceText { get; set; } = "";
    [ObservableProperty] public partial string ConnectionText { get; set; } = "";
    [ObservableProperty] public partial string PendingText { get; set; } = "";
    [ObservableProperty] public partial string? LastError { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial bool IsScanning { get; set; }
    [ObservableProperty] public partial bool CanScanPorts { get; set; }
    [ObservableProperty] public partial string ScanResultText { get; set; } = "";

    // Throwing the queue away is the most destructive thing on this screen: every
    // slip in it belongs to somebody who has already paid. It sat eight pixels
    // from "Pausar / Retomar" wearing the same quiet styling, so it asks first now.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscardLabel))]
    public partial bool DiscardArmed { get; set; }

    public string DiscardLabel => DiscardArmed ? "Apagar mesmo?" : "Apagar fila";

    /// <summary>
    /// Called when the panel opens. Deliberately not done in Refresh: the print
    /// worker raises its change event every three seconds, so disarming there
    /// would cancel the question before anyone could answer it.
    /// </summary>
    public void Disarm() => DiscardArmed = false;

    /// <summary>What the pause button will do, rather than naming both options.</summary>
    public string PauseLabel => IsPaused ? "Retomar impressão" : "Pausar impressão";

    public void Refresh()
    {
        var print = _services.Print;
        var status = print.Status;

        StatusText = status.Message;
        StatusBrush = Colour(status);
        AdviceText = Advice(status, print.RelocatedTo);
        ConnectionText = _services.Printer.Transport.Describe();
        IsPaused = print.IsPaused;
        LastError = print.LastError;
        CanScanPorts = _services.Printer.Transport is SerialTransport;

        PendingText = print.PendingCount switch
        {
            0 => "Nada à espera",
            1 => "1 talão à espera",
            var n => $"{n} talões à espera"
        };

        Queue.Clear();
        foreach (var job in print.Pending()) Queue.Add(new QueuedJobViewModel(job));
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        await Task.Run(() => _services.Print.CheckStatus());
        Refresh();
    }

    [RelayCommand]
    private void RetryNow()
    {
        _services.Print.RetryNow();
        Refresh();
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_services.Print.IsPaused) _services.Print.Resume();
        else _services.Print.Pause();

        Refresh();
    }

    [RelayCommand]
    private void PrintTest()
    {
        _services.Print.EnqueueTest();
        Refresh();
    }

    [RelayCommand]
    private void DiscardQueue()
    {
        var waiting = _services.Print.PendingCount;

        if (waiting == 0)
        {
            ScanResultText = "A fila já está vazia.";
            return;
        }

        if (!DiscardArmed)
        {
            DiscardArmed = true;
            ScanResultText = $"Isto deita fora {waiting} talão(ões) de pessoas que já pagaram, "
                             + "e não voltam. Toque outra vez para confirmar.";
            return;
        }

        DiscardArmed = false;
        _services.Print.DiscardPending();
        Refresh();
        ScanResultText = $"{waiting} talão(ões) apagados sem imprimir.";
    }

    /// <summary>
    /// Scans every COM port and adopts whichever one answers like a printer. This
    /// is the button that replaces the trip into Device Manager.
    /// </summary>
    [RelayCommand]
    private async Task FindPrinter()
    {
        IsScanning = true;
        ScanResultText = "A procurar...";
        Ports.Clear();

        var baud = _services.SerialBaudRate;
        var (probes, moved) = await Task.Run(() =>
            (PrinterLocator.ScanAll(baud), _services.Print.Relocate()));

        foreach (var probe in probes) Ports.Add(new PortProbeViewModel(probe));

        ScanResultText = moved is not null
            ? $"Impressora encontrada em {moved}. Já está a ser usada."
            : probes.Any(p => p.AnsweredAsPrinter)
                ? "A impressora continua na porta configurada."
                : probes.Count == 0
                    ? "Não há portas série nesta máquina. Se a impressora está ligada por USB, "
                      + "veja em Definições do Windows → Impressoras e scanners: se aparecer lá com um nome, "
                      + "mude a Ligação para «Impressora do Windows» e escreva esse nome."
                    : NoPrinterAdvice(probes);

        IsScanning = false;
        Refresh();
    }

    /// <summary>
    /// COM1 and COM2 are almost always the motherboard's own legacy serial ports.
    /// A USB printer that presents as a virtual COM port lands on COM3 or higher,
    /// so finding only the low two means the printer is somewhere else entirely -
    /// usually installed as a Windows print queue.
    /// </summary>
    internal static string NoPrinterAdvice(IReadOnlyList<PortProbe> probes)
    {
        var found = string.Join(", ", probes.Select(p => p.PortName));
        var onlyBuiltIn = probes.All(p => p.PortName is "COM1" or "COM2");

        return onlyBuiltIn
            ? $"Só foram encontradas {found}, que costumam ser as portas da própria motherboard e não a impressora. "
              + "Veja em Definições do Windows → Impressoras e scanners: se a impressora aparecer lá com um nome, "
              + "mude a Ligação para «Impressora do Windows» e escreva esse nome."
            : $"Portas encontradas: {found}. Nenhuma respondeu como impressora. "
              + "Verifique o cabo e se está ligada. Se aparecer em Impressoras e scanners, "
              + "use antes a ligação «Impressora do Windows».";
    }

    private void OnPrintServiceChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Detach() => _services.Print.Changed -= OnPrintServiceChanged;

    private static IBrush Colour(PrinterStatus status) => status.State switch
    {
        PrinterState.Ready or PrinterState.Unknown => Brushes.MediumSeaGreen,
        PrinterState.PaperLow => Brushes.Goldenrod,
        _ => Brushes.IndianRed
    };

    /// <summary>The next thing to actually do, in the words of someone standing at the till.</summary>
    private static string Advice(PrinterStatus status, string? relocatedTo) => status.State switch
    {
        _ when relocatedTo is not null => $"A impressora mudou para {relocatedTo}. Já está a imprimir de novo.",
        PrinterState.PaperOut => "Ponha um rolo novo. Os talões em espera saem sozinhos a seguir.",
        PrinterState.CoverOpen => "Feche a tampa da impressora.",
        PrinterState.Error => "Desligue e volte a ligar a impressora na tomada.",
        PrinterState.Offline => "A impressora está em pausa. Carregue no botão de alimentação de papel.",
        PrinterState.NotFound =>
            "Não respondeu. Verifique o cabo USB e carregue em «Procurar impressora» — o Windows pode ter-lhe dado outra porta COM.",
        PrinterState.PaperLow => "O papel está quase a acabar. Tenha um rolo à mão.",
        PrinterState.Ready => "Tudo em ordem.",
        _ => "Esta ligação não permite consultar o estado da impressora."
    };
}
