using System.Net.Sockets;

namespace SellingPoint.Printing;

/// <summary>
/// Ethernet and WiFi printers. Port 9100 is the near-universal raw printing port,
/// and no driver is involved - the bytes go straight to the print head.
/// </summary>
public sealed class NetworkTransport(string target, int timeoutMs = 5000) : IPrintTransport
{
    public void Send(byte[] data, string preview)
    {
        var (host, port) = Parse(target);

        using var client = new TcpClient { SendTimeout = timeoutMs, ReceiveTimeout = timeoutMs };
        if (!client.ConnectAsync(host, port).Wait(timeoutMs))
            throw new IOException($"A impressora em {host}:{port} não respondeu.");

        using var stream = client.GetStream();
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    public string Describe() => $"Rede {target}";

    /// <summary>Accepts "192.168.1.50" or "192.168.1.50:9100".</summary>
    private static (string Host, int Port) Parse(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Nenhum endereço de impressora configurado.");

        var parts = target.Split(':', 2);
        return (parts[0], parts.Length == 2 && int.TryParse(parts[1], out var port) ? port : 9100);
    }
}
