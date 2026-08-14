using System.IO.Ports;

namespace SellingPoint.Printing;

/// <summary>
/// USB printers that install as a virtual COM port, and genuinely serial ones.
/// 9600 8N1 is what nearly all of them ship with.
///
/// The port is opened and closed per operation rather than held. Windows hands a
/// re-enumerated USB device a new handle - sometimes a whole new COM number - and
/// a long-lived handle just goes stale without saying so.
/// </summary>
public sealed class SerialTransport(string portName, int baudRate = 9600) : IPrintTransport, IStatusQueryable
{
    public string PortName { get; } = portName;
    public int BaudRate { get; } = baudRate;

    public void Send(byte[] data, string preview)
    {
        using var port = Open();
        port.Write(data, 0, data.Length);

        // Closing the port drops DTR immediately; without this the tail of a long
        // ticket can be cut off mid-line.
        Thread.Sleep(200);
    }

    public byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs)
    {
        using var port = Open(timeoutMs);
        port.DiscardInBuffer();
        port.Write(request, 0, request.Length);

        var buffer = new byte[expectedBytes];
        var read = 0;

        try
        {
            while (read < expectedBytes)
            {
                var count = port.Read(buffer, read, expectedBytes - read);
                if (count <= 0) break;
                read += count;
            }
        }
        catch (TimeoutException)
        {
            // A printer that says nothing is an answer in itself.
        }

        return read == 0 ? null : buffer[..read];
    }

    public string Describe() => $"Porta série {PortName} @ {BaudRate}";

    private SerialPort Open(int timeoutMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(PortName))
            throw new InvalidOperationException("Nenhuma porta série configurada.");

        var port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = timeoutMs,
            ReadTimeout = timeoutMs,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true
        };

        port.Open();
        return port;
    }
}
