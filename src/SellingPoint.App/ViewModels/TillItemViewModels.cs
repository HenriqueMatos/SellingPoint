using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;

namespace SellingPoint.App.ViewModels;

/// <summary>
/// Turns a category's single configured colour into the brushes the till draws
/// with. The organizer picks one hex in Gestão; everything else is derived, so
/// there is never a second colour to keep in step with the first.
/// </summary>
public static class CategoryPalette
{
    /// <summary>
    /// Top-lit: the chosen colour at the top falling to a darker version at the
    /// bottom. Flat fills read as printed paper; this reads as a lit button.
    /// </summary>
    public static IBrush Gradient(string? hex, double bottomScale = 0.62)
    {
        var top = Parse(hex);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0),
                new GradientStop(Scale(top, bottomScale), 1)
            }
        };
    }

    public static IBrush Flat(string? hex) => new SolidColorBrush(Parse(hex));

    /// <summary>
    /// Tolerant on purpose, and public because the colour picker in Gestão opens
    /// on whatever is stored - including the hand-typed values older versions let
    /// through, which have to land somewhere rather than throw.
    /// </summary>
    public static Color Parse(string? hex)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? Colors.SteelBlue : Color.Parse(hex);
        }
        catch (FormatException)
        {
            // A hand-typed colour in Gestão should not take the till down.
            return Colors.SteelBlue;
        }
    }

    private static Color Scale(Color color, double factor) => Color.FromRgb(
        (byte)(color.R * factor),
        (byte)(color.G * factor),
        (byte)(color.B * factor));
}

// Each row and button carries its own command, bound straight to itself. The
// alternative - reaching back up to the parent view model from inside an
// ItemsControl - makes for markup that is far easier to get subtly wrong.

public sealed partial class CategoryTabViewModel(Category category) : ViewModelBase
{
    public Category Category { get; } = category;
    public string Name => Category.Name;
    public IBrush Background => CategoryPalette.Gradient(Category.Color, 0.72);

    /// <summary>
    /// A light ring around the chip. Together with the opacity drop on unselected
    /// items it makes the current category obvious from arm's length, which a
    /// standard selection highlight is not on a colour-filled chip.
    /// </summary>
    public IBrush RingBrush { get; } = new SolidColorBrush(Colors.White, 0.4);
}

public sealed partial class ProductButtonViewModel(
    Product product,
    Category? category,
    Action<ProductButtonViewModel> onPress) : ViewModelBase
{
    public Product Product { get; } = product;

    public string Name => Product.Name;
    public string PriceText => Money.Format(Product.PriceCents);
    public IBrush Background => CategoryPalette.Gradient(category?.Color);

    public bool ShowStock => Product.TrackStock;
    public string StockText => Product.StockQty.ToString();

    /// <summary>Faded when out of stock, but still pressable - the setting decides
    /// whether the press is refused, not the button.</summary>
    public double Opacity => Product.TrackStock && Product.StockQty <= 0 ? 0.45 : 1.0;

    [RelayCommand]
    private void Press() => onPress(this);
}

/// <summary>
/// A cart row. The whole collection is rebuilt on every change - a cart holds a
/// handful of lines, and rebuilding removes every chance of the display drifting
/// out of step with the cart it is showing.
/// </summary>
public sealed partial class CartLineViewModel(
    CartLine line,
    Action<CartLineViewModel> onIncrement,
    Action<CartLineViewModel> onDecrement,
    Action<CartLineViewModel> onRemove) : ViewModelBase
{
    public CartLine Line { get; } = line;
    public Product Product => Line.Product;

    public string QtyText => $"{Line.Qty}x";
    public string Name => Line.Product.Name;
    public string TotalText => Money.Format(Line.LineTotalCents);
    public string UnitText => $"{Money.Format(Line.UnitPriceCents)} cada";

    [RelayCommand] private void Increment() => onIncrement(this);
    [RelayCommand] private void Decrement() => onDecrement(this);
    [RelayCommand] private void Remove() => onRemove(this);
}

public sealed partial class QuickTenderViewModel(int cents, Action<QuickTenderViewModel> onPress) : ViewModelBase
{
    public int Cents { get; } = cents;
    public string Label => Money.Format(Cents);

    [RelayCommand]
    private void Press() => onPress(this);
}
