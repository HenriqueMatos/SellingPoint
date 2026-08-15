using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SellingPoint.App.ViewModels;
using SellingPoint.App.Views;

namespace SellingPoint.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --db= runs against a scratch database instead of the real one, and
            // --tab= opens straight onto a screen. Both exist for development and
            // for talking someone through a problem over the phone.
            var databasePath = Argument(desktop.Args, "--db=");
            var services = new AppServices(databasePath);

            // The previous version, left behind by the last swap.
            UpdateInstaller.CleanUp(Environment.ProcessPath);

            desktop.Exit += (_, _) =>
            {
                // Stops the print worker cleanly; anything still queued is on disk
                // and will be picked up next time the app opens.
                services.Dispose();

                // Windows will not let a running program be overwritten, but it will
                // let it be renamed - so the swap happens here, on the way out, and
                // the next launch is the new version.
                services.Installer.ApplyPending(Environment.ProcessPath);
            };

            var viewModel = new MainWindowViewModel(services);

            if (int.TryParse(Argument(desktop.Args, "--tab="), out var tab))
                viewModel.SelectedTab = tab;

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? Argument(string[]? args, string prefix)
        => args?.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
