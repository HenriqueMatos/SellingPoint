using System.IO.Ports;

namespace SellingPoint.Printing;

/// <summary>
/// USB printers that install as a virtual COM port, and genuinely serial ones.
/// 9600 8N1 is what nearly all of them ship with.
/// </summary>
public sealed class SerialTransport(string portName, int baudRate = 9600) : IPrintTransport
{
    public void Send(byte[] data, string preview)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new InvalidOperationException("Nenhuma porta série configurada.");

        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = 5000,
            Handshake = Handshake.None
        };

        port.Open();
        port.Write(data, 0, data.Length);

        // Closing the port drops DTR immediately; without this the tail of a long
        // ticket can be cut off mid-line.
        Thread.Sleep(200);
    }

    public string Describe() => $"Porta série {portName} @ {baudRate}";
}
