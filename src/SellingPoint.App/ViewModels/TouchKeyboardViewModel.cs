using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SellingPoint.App.ViewModels;

public sealed partial class KeyViewModel(string label, double width, Action<KeyViewModel> onPress, string? value = null)
    : ViewModelBase
{
    public string Label { get; } = label;

    /// <summary>What gets typed. Differs from the label on keys like backspace.</summary>
    public string Value { get; } = value ?? label;

    public double Width { get; } = width;

    [RelayCommand]
    private void Press() => onPress(this);
}

/// <summary>
/// A keyboard built into the app rather than borrowed from Windows.
///
/// The machine at the event has no physical keyboard, and Windows' own touch
/// keyboard did not appear. This one cannot fail for the same reasons: it depends
/// on no system setting, it does not steal focus, and because it is docked in the
/// window rather than floating over it, it can never cover the field being typed
/// into - which was the flaw even when the Windows one did show up.
/// </summary>
public partial class TouchKeyboardViewModel : ViewModelBase
{
    private const string Backspace = "⌫";
    private const string Shift = "⇧";
    private const string Done = "✓";
    private const string Space = "espaço";

    private TextBox? _target;

    public ObservableCollection<ObservableCollection<KeyViewModel>> Rows { get; } = [];

    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsNumeric { get; set; }
    [ObservableProperty] public partial bool IsShifted { get; set; }

    /// <summary>
    /// Points the keyboard at whatever text field just took focus, or hides it when
    /// focus moved somewhere that cannot be typed into.
    /// </summary>
    public void Attach(TextBox? target)
    {
        _target = target;

        if (target is null)
        {
            IsVisible = false;
            return;
        }

        // A field marked numeric gets the big number pad. Everything else gets
        // letters - which carry a row of digits too, so a field nobody remembered
        // to mark is still usable.
        IsNumeric = target.Classes.Contains("numeric");
        IsShifted = false;
        IsVisible = true;

        Build();
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        _target = null;
    }

    private void OnKey(KeyViewModel key)
    {
        switch (key.Value)
        {
            case Backspace: DeleteBack(); break;
            case Shift: IsShifted = !IsShifted; Build(); break;
            case Done: Close(); break;
            case Space: Type(" "); break;
            default: Type(key.Value); break;
        }
    }

    private void Type(string text)
    {
        if (_target is null) return;

        var current = _target.Text ?? "";
        var caret = Math.Clamp(_target.CaretIndex, 0, current.Length);

        _target.Text = current.Insert(caret, text);
        _target.CaretIndex = caret + text.Length;

        // A shift applies to one letter, the way it does on a phone.
        if (IsShifted && !IsNumeric)
        {
            IsShifted = false;
            Build();
        }
    }

    private void DeleteBack()
    {
        if (_target is null) return;

        var current = _target.Text ?? "";
        var caret = Math.Clamp(_target.CaretIndex, 0, current.Length);
        if (caret == 0) return;

        _target.Text = current.Remove(caret - 1, 1);
        _target.CaretIndex = caret - 1;
    }

    private void Build()
    {
        Rows.Clear();

        foreach (var row in IsNumeric ? NumericRows() : TextRows())
            Rows.Add(row);
    }

    private ObservableCollection<KeyViewModel> Row(params (string Label, double Width)[] keys)
    {
        var row = new ObservableCollection<KeyViewModel>();
        foreach (var (label, width) in keys) row.Add(new KeyViewModel(label, width, OnKey));

        return row;
    }

    private IEnumerable<ObservableCollection<KeyViewModel>> TextRows()
    {
        const double k = 66;

        yield return Row(Keys("1234567890", k).Append((Backspace, k * 1.6)).ToArray());
        yield return Row(Keys(Case("qwertyuiop"), k).ToArray());
        yield return Row(Keys(Case("asdfghjklç"), k).ToArray());
        yield return Row([(Shift, k * 1.4), .. Keys(Case("zxcvbnm"), k), (",", k), (".", k), ("-", k)]);

        // The accented vowels, without which half the product names cannot be typed.
        yield return Row([
            .. Keys(Case("áéíóúãõâê"), k),
            (Space, k * 3.4),
            (Done, k * 1.6)
        ]);
    }

    private IEnumerable<ObservableCollection<KeyViewModel>> NumericRows()
    {
        const double k = 110;

        yield return Row(("7", k), ("8", k), ("9", k), (Backspace, k));
        yield return Row(("4", k), ("5", k), ("6", k), (",", k));
        yield return Row(("1", k), ("2", k), ("3", k), (Done, k));
        // Wide enough that the bottom row lines up with the three above it.
        yield return Row(("0", k * 3 + 16), (".", k));
    }

    private string Case(string letters) => IsShifted ? letters.ToUpperInvariant() : letters;

    private static IEnumerable<(string Label, double Width)> Keys(string letters, double width)
        => letters.Select(c => (c.ToString(), width));
}
