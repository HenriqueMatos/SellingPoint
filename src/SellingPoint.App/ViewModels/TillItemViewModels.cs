using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;

namespace SellingPoint.App.ViewModels;

// Each row and button carries its own command, bound straight to itself. The
// alternative - reaching back up to the parent view model from inside an
// ItemsControl - makes for markup that is far easier to get subtly wrong.

public sealed partial class CategoryTabViewModel(Category category) : ViewModelBase
{
    public Category Category { get; } = category;
    public string Name => Category.Name;
    public IBrush Background => Brush.Parse(Category.Color);
}

public sealed partial class ProductButtonViewModel(
    Product product,
    Category? category,
    Action<ProductButtonViewModel> onPress) : ViewModelBase
{
    public Product Product { get; } = product;

    public string Name => Product.Name;
    public string PriceText => Money.Format(Product.PriceCents);
    public IBrush Background => Brush.Parse(category?.Color ?? "#3A7BD5");

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
