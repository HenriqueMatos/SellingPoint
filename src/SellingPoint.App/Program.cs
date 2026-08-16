using Avalonia;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SellingPoint.App;

public sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // One till per database.
        //
        // On a touch screen a second tap on the icon is easy to make and hard to
        // notice, and two copies open on the same database is worse than none:
        // both hand out ticket numbers from the same count, both may open a
        // session, and a product saved in one silently writes its own idea of the
        // stock over what the other has been selling.
        //
        // The second copy simply does not start. It has nothing to say that would
        // help - the first one is already on screen, in front of the person who
        // tapped.
        using var onlyOne = new Mutex(initiallyOwned: true, InstanceName(args), out var isOnlyOne);
        if (!isOnlyOne) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Keyed on the database, so --db= still opens a scratch copy alongside the
    /// real till - that is what the flag is for.
    ///
    /// Hashed with SHA-256 rather than string.GetHashCode: since .NET Core that
    /// hash is randomised per process, so the two copies this is meant to catch
    /// would compute different names and both start, which is the exact failure
    /// this guards against and would never be noticed.
    /// </summary>
    public static string InstanceName(string[]? args)
    {
        var database = args?.FirstOrDefault(a => a.StartsWith("--db=", StringComparison.Ordinal))?["--db=".Length..];
        var key = (database ?? "predefinida").ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return $"SenhasDoCalvario-{Convert.ToHexString(digest)[..16]}";
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
