namespace SellingPoint.Printing;

public enum PrinterState
{
    /// <summary>The printer works but does not answer status queries. Print anyway.</summary>
    Unknown,
    Ready,
    PaperLow,
    PaperOut,
    CoverOpen,
    Offline,
    Error,

    /// <summary>Nothing answered at all - unplugged, powered off, or on another port.</summary>
    NotFound
}

public sealed record PrinterStatus(PrinterState State, string Message)
{
    /// <summary>
    /// Whether to attempt a print. <see cref="PrinterState.Unknown"/> counts as yes:
    /// plenty of cheap printers ignore status queries entirely, and refusing to
    /// print to one because it will not talk about itself is worse than useless.
    /// </summary>
    public bool CanPrint => State is PrinterState.Ready or PrinterState.PaperLow or PrinterState.Unknown;

    public static readonly PrinterStatus Unknown = new(PrinterState.Unknown, "Estado desconhecido");
    public static readonly PrinterStatus NotSupported = new(PrinterState.Unknown, "Esta ligação não permite consultar o estado");
}

/// <summary>
/// A transport that can read back as well as write. Serial and network can;
/// the Windows spooler and the file transport cannot, which is the reason the
/// diagnostics are weaker on those two.
/// </summary>
public interface IStatusQueryable
{
    /// <summary>Sends a request and reads up to <paramref name="expectedBytes"/>. Null when nothing answers.</summary>
    byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs);
}

/// <summary>
/// ESC/POS real-time status. DLE EOT jumps the print buffer, so it answers even
/// while a long ticket is still coming out - which is exactly when you want to
/// know whether the paper ran out.
/// </summary>
public static class EscPosStatus
{
    private const byte PrinterStatus = 1;
    private const byte OfflineCause = 2;
    private const byte PaperSensor = 4;

    public static PrinterStatus Query(IStatusQueryable transport, int timeoutMs = 400)
    {
        var printer = Read(transport, PrinterStatus, timeoutMs);
        if (printer is null)
            return new PrinterStatus(PrinterState.NotFound, "A impressora não respondeu");

        // Paper first: running out also reports as offline, and "sem papel" is the
        // message that tells someone what to actually do about it.
        if (Read(transport, PaperSensor, timeoutMs) is { } paper)
        {
            if ((paper & 0x60) == 0x60) return new PrinterStatus(PrinterState.PaperOut, "Sem papel");
            if ((paper & 0x0C) == 0x0C) return new PrinterStatus(PrinterState.PaperLow, "Papel quase a acabar");
        }

        if ((printer.Value & 0x08) != 0)
        {
            if (Read(transport, OfflineCause, timeoutMs) is { } offline)
            {
                if ((offline & 0x04) != 0) return new PrinterStatus(PrinterState.CoverOpen, "Tampa aberta");
                if ((offline & 0x20) != 0) return new PrinterStatus(PrinterState.PaperOut, "Sem papel");
                if ((offline & 0x40) != 0) return new PrinterStatus(PrinterState.Error, "Erro na impressora");
            }

            return new PrinterStatus(PrinterState.Offline, "Impressora em pausa");
        }

        return new PrinterStatus(PrinterState.Ready, "Pronta");
    }

    /// <summary>True when the byte carries the fixed pattern every real status byte has.</summary>
    public static bool IsStatusByte(byte value) => (value & 0x93) == 0x12;

    /// <summary>
    /// A single status byte, or null. Anything that comes back without the fixed
    /// bit pattern is a different device on that port, or line noise.
    /// </summary>
    private static byte? Read(IStatusQueryable transport, byte kind, int timeoutMs)
    {
        try
        {
            var answer = transport.Exchange([0x10, 0x04, kind], 1, timeoutMs);
            return answer is [var value] && IsStatusByte(value) ? value : null;
        }
        catch (Exception)
        {
            // A port that has vanished mid-query is a NotFound, not a crash.
            return null;
        }
    }
}
