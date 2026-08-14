using System.Diagnostics;

namespace SellingPoint.App;

/// <summary>
/// Brings up the Windows touch keyboard when a text field takes focus.
///
/// The till runs fullscreen on a machine with no physical keyboard, and Windows
/// does not reliably raise the touch keyboard for a borderless desktop app on its
/// own. Launching TabTip is the standard way to ask for it from outside the
/// Store-app world.
///
/// Two things are outside our control and worth knowing when this does not work:
/// Windows has a setting for it (Definições, Hora e Idioma, Escrita, Teclado
/// tátil) that often needs enabling, and the keyboard covers the lower part of the
/// screen without resizing the app behind it.
/// </summary>
public static class TouchKeyboard
{
    private static readonly string TabTipPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        "Microsoft Shared", "ink", "TabTip.exe");

    /// <summary>
    /// Asks Windows for the touch keyboard. Silent everywhere else, and silent on
    /// failure: not getting a keyboard is a nuisance, but an exception thrown from
    /// a focus handler would take the till down mid-sale.
    /// </summary>
    public static void Show()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (!File.Exists(TabTipPath)) return;

            Process.Start(new ProcessStartInfo(TabTipPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Nothing useful to do, and nothing worth interrupting a sale for.
        }
    }
}
