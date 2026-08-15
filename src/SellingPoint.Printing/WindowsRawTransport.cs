using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SellingPoint.Printing;

/// <summary>
/// The common case for cheap USB thermal printers on Windows: they install as a
/// print queue with no COM port, so the only way in is the spooler's RAW datatype.
/// The driver is bypassed entirely - these are still ESC/POS bytes.
/// </summary>
public sealed class WindowsRawTransport(string printerName) : IPrintTransport
{
    public void Send(byte[] data, string preview)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "A impressão pela fila do Windows só funciona no Windows. Use ficheiros ou rede para testar.");

        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Nenhuma impressora do Windows configurada.");

        SendToQueue(printerName, data);
    }

    public string Describe() => $"Windows: {printerName}";

    [SupportedOSPlatform("windows")]
    private static void SendToQueue(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw Failure($"Não foi possível abrir a impressora '{printerName}'");

        var buffer = IntPtr.Zero;
        try
        {
            // What the operator sees in the Windows print queue when a slip is
            // spooling, so it says which program put it there.
            var document = new DocInfo { DocName = "Senhas do Calvário", DataType = "RAW" };

            if (!StartDocPrinter(printer, 1, ref document)) throw Failure("StartDocPrinter falhou");
            if (!StartPagePrinter(printer)) throw Failure("StartPagePrinter falhou");

            buffer = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, buffer, data.Length);

            if (!WritePrinter(printer, buffer, data.Length, out var written) || written != data.Length)
                throw Failure($"Só foram escritos {written} de {data.Length} bytes");

            EndPagePrinter(printer);
            EndDocPrinter(printer);
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            ClosePrinter(printer);
        }
    }

    private static IOException Failure(string what) =>
        new($"{what}: {Marshal.GetLastWin32Error()}");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr printer, int level, ref DocInfo document);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr bytes, int count, out int written);
}
