using System.Net.Sockets;

namespace SellingPoint.Printing;

/// <summary>
/// Ethernet and WiFi printers. Port 9100 is the near-universal raw printing port,
/// and no driver is involved - the bytes go straight to the print head.
///
/// There is no port enumeration on this path, so the whole class of "Windows gave
/// it a different COM number" failures does not exist here.
/// </summary>
public sealed class NetworkTransport(string target, int timeoutMs = 5000) : IPrintTransport, IStatusQueryable
{
    public string Target { get; } = target;

    public void Send(byte[] data, string preview)
    {
        using var client = Connect(timeoutMs);
        using var stream = client.GetStream();

        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    public byte[]? Exchange(byte[] request, int expectedBytes, int timeoutMs)
    {
        using var client = Connect(timeoutMs);
        using var stream = client.GetStream();

        stream.ReadTimeout = timeoutMs;
        stream.Write(request, 0, request.Length);
        stream.Flush();

        var buffer = new byte[expectedBytes];
        var read = 0;

        try
        {
            while (read < expectedBytes)
            {
                var count = stream.Read(buffer, read, expectedBytes - read);
                if (count <= 0) break;
                read += count;
            }
        }
        catch (IOException)
        {
            // Read timeout arrives as an IOException on a network stream.
        }

        return read == 0 ? null : buffer[..read];
    }

    public string Describe() => $"Rede {Target}";

    private TcpClient Connect(int timeout)
    {
        var (host, port) = Parse(Target);
        var client = new TcpClient { SendTimeout = timeout, ReceiveTimeout = timeout };

        try
        {
            if (!client.ConnectAsync(host, port).Wait(timeout))
                throw new IOException($"A impressora em {host}:{port} não respondeu.");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    /// <summary>Accepts "192.168.1.50" or "192.168.1.50:9100".</summary>
    private static (string Host, int Port) Parse(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Nenhum endereço de impressora configurado.");

        var parts = target.Split(':', 2);
        return (parts[0], parts.Length == 2 && int.TryParse(parts[1], out var port) ? port : 9100);
    }
}
