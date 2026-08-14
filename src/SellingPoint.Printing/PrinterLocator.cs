using System.IO.Ports;

namespace SellingPoint.Printing;

public sealed record PortProbe(string PortName, PrinterState State, string Message)
{
    public bool AnsweredAsPrinter => State is not PrinterState.NotFound;
}

/// <summary>
/// Finds the printer when Windows has moved it. A USB printer that briefly
/// re-enumerates - a knocked cable, a power dip, a different USB socket - comes
/// back on a different COM number, and a configured "COM3" then points at nothing.
/// </summary>
public static class PrinterLocator
{
    public static IReadOnlyList<string> AvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames().Distinct().Order().ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Opens a port and asks whatever is on it for its status. A port in use by
    /// something else, or with nothing behind it, comes back as NotFound rather
    /// than throwing.
    /// </summary>
    public static PortProbe Probe(string portName, int baudRate = 9600, int timeoutMs = 400)
    {
        try
        {
            var status = EscPosStatus.Query(new SerialTransport(portName, baudRate), timeoutMs);
            return new PortProbe(portName, status.State, status.Message);
        }
        catch (Exception e)
        {
            return new PortProbe(portName, PrinterState.NotFound, PrinterErrors.Describe(e));
        }
    }

    /// <summary>Every port on the machine, for the diagnostics screen.</summary>
    public static IReadOnlyList<PortProbe> ScanAll(int baudRate = 9600, int timeoutMs = 400)
        => AvailablePorts().Select(port => Probe(port, baudRate, timeoutMs)).ToArray();

    /// <summary>
    /// The configured port if it still answers, otherwise the first other port that
    /// does. Null when nothing on the machine responds like a printer.
    ///
    /// Other ports are only probed once the configured one has failed, so a working
    /// till never writes to a port belonging to some other device.
    /// </summary>
    public static string? Locate(string? preferred, int baudRate = 9600, int timeoutMs = 400)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Probe(preferred, baudRate, timeoutMs).AnsweredAsPrinter)
            return preferred;

        foreach (var port in AvailablePorts())
        {
            if (string.Equals(port, preferred, StringComparison.OrdinalIgnoreCase)) continue;
            if (Probe(port, baudRate, timeoutMs).AnsweredAsPrinter) return port;
        }

        return null;
    }
}
