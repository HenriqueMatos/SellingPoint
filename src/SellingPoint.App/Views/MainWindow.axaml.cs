using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SellingPoint.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Tunnelling rather than a plain KeyDown: F11 has to reach the window even
        // when a text box has focus, or the key would work on the till screen but
        // not while someone is editing a price in Gestão.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // One handler for every text field in the app, rather than wiring each one.
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);

        UpdateToggleLabel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.F11) return;

        ToggleFullScreen();
        e.Handled = true;
    }

    /// <summary>
    /// The machine at the event has no physical keyboard, so tapping a field has
    /// to bring one up.
    /// </summary>
    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is TextBox) TouchKeyboard.Show();
    }

    private void OnToggleFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Maximized
            : WindowState.FullScreen;

    /// <summary>
    /// Keeps the button honest when the window state changes by any route - the
    /// key, the button, or the green traffic light on macOS.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty) UpdateToggleLabel();
    }

    private void UpdateToggleLabel()
    {
        // Fires once during construction before the control exists.
        if (FullScreenToggle is null) return;

        FullScreenToggle.Content = WindowState == WindowState.FullScreen
            ? "Sair do ecrã inteiro"
            : "Ecrã inteiro";
    }
}
