using SellingPoint.Core;
using SellingPoint.Data;
using SellingPoint.Printing;

namespace SellingPoint.App;

/// <summary>
/// Everything that goes to paper goes through here. Slips are queued first and
/// sent second, so a printer that is out of paper, unplugged, or has been handed
/// a new COM number by Windows never costs a ticket - it costs a delay.
///
/// Deliberately free of any Avalonia reference: it runs on a background thread
/// and raises a plain event, and the view models marshal that onto the UI thread.
/// </summary>
public sealed class PrintService : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeepPrintedFor = TimeSpan.FromDays(7);

    private readonly PrintQueueRepository _queue;
    private readonly SettingsRepository _settings;
    private readonly TicketPrinter _printer;

    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;

    private int _consecutiveFailures;
    private DateTime _nextAttempt = DateTime.MinValue;
    private DateTime _lastHealthCheck = DateTime.MinValue;

    public PrintService(PrintQueueRepository queue, SettingsRepository settings, TicketPrinter printer)
    {
        _queue = queue;
        _settings = settings;
        _printer = printer;

        _queue.PurgePrintedBefore(DateTime.Now - KeepPrintedFor);
        PendingCount = _queue.PendingCount();
    }

    /// <summary>Raised on the worker thread whenever status or the queue changes.</summary>
    public event Action? Changed;

    public PrinterStatus Status { get; private set; } = PrinterStatus.Unknown;
    public int PendingCount { get; private set; }
    public bool IsPaused { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Set when the printer was found on a different port than the configured one.</summary>
    public string? RelocatedTo { get; private set; }

    public void Start() => _worker ??= Task.Run(() => LoopAsync(_stopping.Token));

    /// <summary>Queues every slip of a sale and asks the worker to try immediately.</summary>
    public int Enqueue(Sale sale)
    {
        var composed = _printer.Compose(sale);
        Enqueue(composed, sale.Id);
        return composed.Count;
    }

    public void EnqueueText(string title, IEnumerable<string> body)
        => Enqueue([_printer.ComposeText(title, body)], saleId: null);

    public void EnqueueTest() => Enqueue([_printer.ComposeTest()], saleId: null);

    public void Pause()
    {
        IsPaused = true;
        Notify();
    }

    public void Resume()
    {
        IsPaused = false;
        _consecutiveFailures = 0;
        _nextAttempt = DateTime.MinValue;
        Wake();
    }

    /// <summary>"Tentar agora" - clears the backoff and pokes the worker.</summary>
    public void RetryNow()
    {
        _nextAttempt = DateTime.MinValue;
        _lastHealthCheck = DateTime.MinValue;
        Wake();
    }

    public void DiscardPending()
    {
        _queue.DiscardPending();
        PendingCount = _queue.PendingCount();
        Notify();
    }

    public List<PrintJob> Pending() => _queue.Pending();

    /// <summary>Reads the printer's status now, on the calling thread.</summary>
    public PrinterStatus CheckStatus()
    {
        RefreshStatus();
        Notify();
        return Status;
    }

    /// <summary>
    /// Hunts for the printer across every COM port and adopts the one that answers.
    /// Exposed for the diagnostics screen so the operator can force it.
    /// </summary>
    public string? Relocate()
    {
        var moved = TryRelocate();
        RefreshStatus();
        Notify();
        return moved;
    }

    private void Enqueue(IReadOnlyList<ComposedSlip> slips, int? saleId)
    {
        var now = DateTime.Now;

        _queue.Enqueue(slips.Select(slip => new PrintJob
        {
            SaleId = saleId,
            Title = slip.Title,
            Payload = slip.Payload,
            Preview = slip.Preview,
            CreatedAt = now
        }).ToList());

        // Read once for the whole sale rather than once per slip. Updated here
        // rather than left to the worker's next tick: the till reads this the
        // instant a sale completes, to say how many slips are waiting.
        PendingCount = _queue.PendingCount();

        // A ticket that has just been rung up is tried now. Without this it serves
        // out whatever backoff an earlier failure left behind - up to thirty
        // seconds of nothing happening, with a printer that may already be fine
        // again. The failure count is deliberately left alone: it is what makes
        // TryRelocate fire every third failure, and clearing it would blind that.
        _nextAttempt = DateTime.MinValue;
        Wake();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // The worker must not die: a dead worker means a queue that never
                // drains and nobody being told why.
                LastError = e.Message;
                Notify();
            }

            try
            {
                await _wake.WaitAsync(Poll, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Tick()
    {
        PendingCount = _queue.PendingCount();

        if (IsPaused || PendingCount == 0)
        {
            if (!IsPaused && DateTime.Now - _lastHealthCheck > HealthCheckInterval) RefreshStatus();
            Notify();
            return;
        }

        if (DateTime.Now < _nextAttempt)
        {
            Notify();
            return;
        }

        if (_queue.NextPending() is not { } job)
        {
            Notify();
            return;
        }

        try
        {
            _printer.Send(new ComposedSlip(job.Title, job.Payload, job.Preview));

            _queue.MarkPrinted(job.Id, DateTime.Now);
            _consecutiveFailures = 0;
            _nextAttempt = DateTime.MinValue;
            LastError = null;
            RelocatedTo = null;

            // Straight on to the next one rather than waiting out the poll interval.
            PendingCount = _queue.PendingCount();
            if (PendingCount > 0) Wake();
        }
        catch (Exception e)
        {
            var reason = PrinterErrors.Describe(e);

            _consecutiveFailures++;
            _queue.MarkFailed(job.Id, reason);
            LastError = reason;

            RefreshStatus();

            // Three failures in a row is the signature of the port having moved
            // rather than of paper having run out.
            if (_consecutiveFailures % 3 == 0) TryRelocate();

            _nextAttempt = DateTime.Now.AddSeconds(BackoffSeconds(_consecutiveFailures));
            PendingCount = _queue.PendingCount();
        }

        Notify();
    }

    private static int BackoffSeconds(int failures) => failures switch
    {
        1 => 2,
        2 => 5,
        3 => 10,
        _ => 30
    };

    private void RefreshStatus()
    {
        _lastHealthCheck = DateTime.Now;
        Status = _printer.Transport is IStatusQueryable queryable
            ? EscPosStatus.Query(queryable)
            : PrinterStatus.NotSupported;
    }

    /// <summary>
    /// Only serial printers can move: a network address and a Windows queue name
    /// stay where they were put.
    /// </summary>
    private string? TryRelocate()
    {
        if (_printer.Transport is not SerialTransport serial) return null;

        var found = PrinterLocator.Locate(serial.PortName, serial.BaudRate);
        if (found is null || string.Equals(found, serial.PortName, StringComparison.OrdinalIgnoreCase))
            return null;

        _printer.Transport = new SerialTransport(found, serial.BaudRate);
        _settings.Set(SettingKeys.PrinterTarget, found);

        RelocatedTo = found;
        _consecutiveFailures = 0;
        _nextAttempt = DateTime.MinValue;

        return found;
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already awake; nothing to do.
        }
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        _stopping.Cancel();

        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutting down; a worker that will not stop is not worth blocking on.
        }

        _stopping.Dispose();
        _wake.Dispose();
    }
}
