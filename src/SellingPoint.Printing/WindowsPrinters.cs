using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SellingPoint.Printing;

/// <summary>
/// The printers Windows itself knows about, so one can be picked from a list
/// rather than typed. A name typed with one space too many fails silently, which
/// is a miserable thing to debug standing at a counter.
///
/// Uses the same winspool the raw transport prints through, so no new dependency.
/// </summary>
public static class WindowsPrinters
{
    /// <summary>Installed and connected printers. Empty off Windows, and empty rather than throwing on failure.</summary>
    public static IReadOnlyList<string> List()
    {
        if (!OperatingSystem.IsWindows()) return [];

        try
        {
            return Enumerate();
        }
        catch (Exception)
        {
            // A settings screen that cannot list printers is a nuisance; one that
            // crashes the till is worse.
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> Enumerate()
    {
        const uint localAndConnections = 0x2 | 0x4;
        const uint level = 4;

        // First call sizes the buffer, second fills it.
        EnumPrinters(localAndConnections, null, level, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0) return [];

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(localAndConnections, null, level, buffer, needed, out _, out var count))
                return [];

            var size = Marshal.SizeOf<PrinterInfo4>();
            var names = new List<string>((int)count);

            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(IntPtr.Add(buffer, i * size));
                if (!string.IsNullOrWhiteSpace(info.PrinterName)) names.Add(info.PrinterName);
            }

            return names.Distinct().Order().ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string PrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags, string? name, uint level, IntPtr printerEnum,
        uint bufferBytes, out uint bytesNeeded, out uint returned);
}
