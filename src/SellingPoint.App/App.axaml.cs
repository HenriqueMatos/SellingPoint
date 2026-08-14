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

            // Stops the print worker cleanly; anything still queued is on disk and
            // will be picked up next time the app opens.
            desktop.Exit += (_, _) => services.Dispose();

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
