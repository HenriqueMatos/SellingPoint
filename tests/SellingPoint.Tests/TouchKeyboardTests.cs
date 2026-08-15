using Avalonia.Controls;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// The machine at the event has no physical keyboard and Windows' own did not
/// appear, so this one has to work. It types into whatever field has focus.
/// </summary>
public class TouchKeyboardTests
{
    private static (TouchKeyboardViewModel Keyboard, TextBox Field) Attached(string? classes = null)
    {
        var field = new TextBox();
        if (classes is not null) field.Classes.Add(classes);

        var keyboard = new TouchKeyboardViewModel();
        keyboard.Attach(field);

        return (keyboard, field);
    }

    private static void Tap(TouchKeyboardViewModel keyboard, string label)
    {
        var key = keyboard.Rows.SelectMany(r => r).FirstOrDefault(k => k.Label == label);
        Assert.True(key is not null, $"No key labelled '{label}'");

        key!.PressCommand.Execute(null);
    }

    [Fact]
    public void It_stays_out_of_the_way_until_a_field_is_touched()
    {
        var keyboard = new TouchKeyboardViewModel();

        Assert.False(keyboard.IsVisible);

        keyboard.Attach(new TextBox());
        Assert.True(keyboard.IsVisible);

        // Focus moved to something that cannot be typed into.
        keyboard.Attach(null);
        Assert.False(keyboard.IsVisible);
    }

    [Fact]
    public void Typing_lands_in_the_focused_field()
    {
        var (keyboard, field) = Attached();

        Tap(keyboard, "c");
        Tap(keyboard, "a");
        Tap(keyboard, "f");

        Assert.Equal("caf", field.Text);
        Assert.Equal(3, field.CaretIndex);
    }

    [Fact]
    public void Portuguese_needs_its_accents()
    {
        // Half the product names cannot be typed without these.
        var (keyboard, field) = Attached();

        Tap(keyboard, "á");
        Tap(keyboard, "ã");
        Tap(keyboard, "ç");
        Tap(keyboard, "ê");

        Assert.Equal("áãçê", field.Text);
    }

    [Fact]
    public void Backspace_removes_the_character_before_the_cursor()
    {
        var (keyboard, field) = Attached();
        Tap(keyboard, "a");
        Tap(keyboard, "b");

        Tap(keyboard, "⌫");

        Assert.Equal("a", field.Text);
        Assert.Equal(1, field.CaretIndex);
    }

    [Fact]
    public void Backspace_on_an_empty_field_does_nothing()
    {
        var (keyboard, field) = Attached();

        Tap(keyboard, "⌫");

        Assert.True(string.IsNullOrEmpty(field.Text));
    }

    [Fact]
    public void Shift_capitalises_one_letter_and_then_lets_go()
    {
        var (keyboard, field) = Attached();

        Tap(keyboard, "⇧");
        Tap(keyboard, "C");
        Tap(keyboard, "e");

        Assert.Equal("Ce", field.Text);
        Assert.False(keyboard.IsShifted);
    }

    [Fact]
    public void Typing_inserts_at_the_cursor_rather_than_at_the_end()
    {
        var (keyboard, field) = Attached();
        field.Text = "Cerveja";
        field.CaretIndex = 0;

        Tap(keyboard, "1");

        Assert.Equal("1Cerveja", field.Text);
    }

    [Fact]
    public void A_field_marked_numeric_gets_the_number_pad()
    {
        var (keyboard, _) = Attached("numeric");

        Assert.True(keyboard.IsNumeric);
        Assert.Contains(keyboard.Rows.SelectMany(r => r), k => k.Label == "7");
        Assert.DoesNotContain(keyboard.Rows.SelectMany(r => r), k => k.Label == "q");
    }

    [Fact]
    public void An_ordinary_field_still_offers_digits()
    {
        // So a field nobody remembered to mark as numeric is still usable.
        var (keyboard, field) = Attached();

        Tap(keyboard, "1");
        Tap(keyboard, "5");
        Tap(keyboard, "0");

        Assert.Equal("150", field.Text);
    }

    [Fact]
    public void The_number_pad_has_a_comma_because_prices_are_written_with_one()
    {
        var (keyboard, field) = Attached("numeric");

        Tap(keyboard, "1");
        Tap(keyboard, ",");
        Tap(keyboard, "5");
        Tap(keyboard, "0");

        Assert.Equal("1,50", field.Text);
    }

    [Fact]
    public void Done_puts_the_keyboard_away()
    {
        var (keyboard, _) = Attached();

        Tap(keyboard, "✓");

        Assert.False(keyboard.IsVisible);
    }

    [Fact]
    public void Typing_after_it_was_put_away_does_not_crash()
    {
        var (keyboard, field) = Attached();
        var key = keyboard.Rows.SelectMany(r => r).First(k => k.Label == "a");

        Tap(keyboard, "✓");
        key.PressCommand.Execute(null);   // a stale key from before it closed

        Assert.True(string.IsNullOrEmpty(field.Text));
    }
}
