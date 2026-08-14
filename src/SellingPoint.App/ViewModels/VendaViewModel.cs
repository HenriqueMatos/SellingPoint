using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Printing;

namespace SellingPoint.App.ViewModels;

/// <summary>The till. Press products, take money, print.</summary>
public partial class VendaViewModel(AppServices services) : ViewModelBase
{
    private readonly Cart _cart = new();
    private List<Category> _categories = [];
    private List<Product> _products = [];
    private Session? _session;

    public ObservableCollection<CategoryTabViewModel> Categories { get; } = [];
    public ObservableCollection<ProductButtonViewModel> Products { get; } = [];
    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];
    public ObservableCollection<QuickTenderViewModel> QuickTenders { get; } = [];

    [ObservableProperty] public partial CategoryTabViewModel? SelectedCategory { get; set; }
    [ObservableProperty] public partial string TotalText { get; set; } = Money.Format(0);
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsCartEmpty { get; set; } = true;

    [ObservableProperty] public partial string SessionText { get; set; } = "";
    [ObservableProperty] public partial bool HasOpenSession { get; set; }
    [ObservableProperty] public partial bool CanReprint { get; set; }

    [ObservableProperty] public partial bool IsPreviewVisible { get; set; }
    [ObservableProperty] public partial string PreviewText { get; set; } = "";

    [ObservableProperty] public partial bool IsCashPanelOpen { get; set; }
    [ObservableProperty] public partial string CashEntry { get; set; } = "";

    [ObservableProperty] public partial bool IsSessionPanelOpen { get; set; }
    [ObservableProperty] public partial string SessionNameEntry { get; set; } = "";
    [ObservableProperty] public partial string SessionFloatEntry { get; set; } = "";

    public int CashReceivedCents => int.TryParse(CashEntry, out var cents) ? cents : 0;
    public string CashReceivedText => Money.Format(CashReceivedCents);
    public string ChangeText => Tender.TryChange(_cart.TotalCents, CashReceivedCents, out var change)
        ? Money.Format(change)
        : "--";
    public bool CanConfirmCash => !_cart.IsEmpty && CashReceivedCents >= _cart.TotalCents;

    public void Load()
    {
        _cart.OutOfStock = services.OutOfStock;
        _categories = services.Catalog.GetCategories();
        _products = services.Catalog.GetProducts();

        var previouslySelected = SelectedCategory?.Category.Id;
        Categories.Clear();
        foreach (var category in _categories) Categories.Add(new CategoryTabViewModel(category));

        SelectedCategory = Categories.FirstOrDefault(c => c.Category.Id == previouslySelected)
                           ?? Categories.FirstOrDefault();

        RefreshProducts();
        RefreshSession();
        RefreshCart();
    }

    partial void OnSelectedCategoryChanged(CategoryTabViewModel? value) => RefreshProducts();

    partial void OnCashEntryChanged(string value)
    {
        OnPropertyChanged(nameof(CashReceivedText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(CanConfirmCash));
    }

    private void Add(Product product)
    {
        StatusMessage = _cart.Add(product) switch
        {
            AddResult.Blocked => $"{product.Name}: sem stock.",
            AddResult.AddedBeyondStock => $"{product.Name}: stock esgotado, a vender na mesma.",
            _ => ""
        };

        RefreshCart();
    }

    private void Decrement(Product product)
    {
        _cart.Decrement(product);
        RefreshCart();
    }

    private void Remove(Product product)
    {
        _cart.Remove(product);
        RefreshCart();
    }

    [RelayCommand]
    private void ClearCart()
    {
        _cart.Clear();
        StatusMessage = "";
        RefreshCart();
    }

    [RelayCommand]
    private void OpenCashPanel()
    {
        if (_cart.IsEmpty || !HasOpenSession) return;

        CashEntry = "";
        QuickTenders.Clear();
        foreach (var amount in Tender.QuickTender(_cart.TotalCents))
            QuickTenders.Add(new QuickTenderViewModel(amount, t => CashEntry = t.Cents.ToString()));

        IsCashPanelOpen = true;
    }

    [RelayCommand]
    private void CloseCashPanel() => IsCashPanelOpen = false;

    /// <summary>Digits shift in from the right, so "1050" is 10,50 EUR - no decimal key.</summary>
    [RelayCommand]
    private void CashDigit(string digit) => CashEntry = (CashEntry + digit).TrimStart('0');

    [RelayCommand]
    private void CashClear() => CashEntry = "";

    [RelayCommand]
    private Task ConfirmCash() => Complete(PaymentMethod.Cash, CashReceivedCents);

    [RelayCommand]
    private Task PayCard() => Complete(PaymentMethod.Card, 0);

    [RelayCommand]
    private async Task ReprintLast()
    {
        if (_session is null) return;

        var last = services.Sales.GetLastSale(_session.Id);
        if (last is null)
        {
            StatusMessage = "Ainda não há vendas nesta sessão.";
            return;
        }

        StatusMessage = await PrintAsync(last)
            ? $"Talão {TicketBuilder.Reference(last.TicketNumber)} reimpresso."
            : StatusMessage;
    }

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewVisible = !IsPreviewVisible;
        RefreshPreview();
    }

    [RelayCommand]
    private void OpenSessionPanel()
    {
        SessionNameEntry = $"Sessão {DateTime.Now:dd/MM/yyyy}";
        SessionFloatEntry = "";
        IsSessionPanelOpen = true;
    }

    [RelayCommand]
    private void CloseSessionPanel() => IsSessionPanelOpen = false;

    [RelayCommand]
    private void ConfirmOpenSession()
    {
        if (string.IsNullOrWhiteSpace(SessionNameEntry))
        {
            StatusMessage = "Dê um nome à sessão.";
            return;
        }

        // An empty float is a legitimate answer, not an error.
        if (!Money.TryParseEuros(SessionFloatEntry, out var floatCents)) floatCents = 0;

        services.Sales.OpenSession(SessionNameEntry.Trim(), floatCents, DateTime.Now);

        IsSessionPanelOpen = false;
        RefreshSession();
        StatusMessage = $"Sessão aberta com {Money.Format(floatCents)} de fundo de caixa.";
    }

    private async Task Complete(PaymentMethod method, int cashReceivedCents)
    {
        if (_session is null)
        {
            StatusMessage = "Abra uma sessão antes de vender.";
            return;
        }

        if (_cart.IsEmpty) return;

        Sale sale;
        try
        {
            sale = SaleFactory.Build(_cart, _categories.ToDictionary(c => c.Id),
                method, cashReceivedCents, DateTime.Now);
        }
        catch (InvalidOperationException e)
        {
            StatusMessage = e.Message;
            return;
        }

        // Recorded before printing: a printer that jams, runs out of paper or has
        // been unplugged must never cost the till a sale.
        services.Sales.Save(sale, _session.Id);

        var reference = TicketBuilder.Reference(sale.TicketNumber);
        var change = method == PaymentMethod.Cash && sale.ChangeCents > 0
            ? $" Troco {Money.Format(sale.ChangeCents)}."
            : "";

        StatusMessage = await PrintAsync(sale)
            ? $"Talão {reference} impresso.{change}"
            : $"Venda {reference} gravada.{change} {StatusMessage}";

        _cart.Clear();
        IsCashPanelOpen = false;
        CashEntry = "";
        CanReprint = true;

        // Stock moved, so the buttons need their counts back.
        _products = services.Catalog.GetProducts();
        RefreshProducts();
        RefreshCart();
    }

    /// <summary>
    /// Off the UI thread: a network printer that has gone away blocks for the
    /// whole connect timeout, and a frozen till in front of a queue is its own
    /// kind of failure.
    /// </summary>
    private async Task<bool> PrintAsync(Sale sale)
    {
        try
        {
            await Task.Run(() => services.Printer.Print(sale));
            return true;
        }
        catch (Exception e)
        {
            StatusMessage = $"Falha na impressão: {e.Message}";
            return false;
        }
    }

    private void RefreshProducts()
    {
        Products.Clear();
        if (SelectedCategory is null) return;

        var category = SelectedCategory.Category;
        foreach (var product in _products.Where(p => p.CategoryId == category.Id))
            Products.Add(new ProductButtonViewModel(product, category, b => Add(b.Product)));
    }

    private void RefreshCart()
    {
        CartLines.Clear();
        foreach (var line in _cart.Lines)
        {
            CartLines.Add(new CartLineViewModel(line,
                l => Add(l.Product), l => Decrement(l.Product), l => Remove(l.Product)));
        }

        TotalText = Money.Format(_cart.TotalCents);
        IsCartEmpty = _cart.IsEmpty;

        OnPropertyChanged(nameof(CanConfirmCash));
        OnPropertyChanged(nameof(ChangeText));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!IsPreviewVisible) return;

        if (_cart.IsEmpty)
        {
            PreviewText = "(carrinho vazio)";
            return;
        }

        var preview = SaleFactory.Build(_cart, _categories.ToDictionary(c => c.Id),
            PaymentMethod.Cash, _cart.TotalCents, DateTime.Now);
        preview.TicketNumber = (_session is null ? 0 : services.Sales.GetLastSale(_session.Id)?.TicketNumber ?? 0) + 1;

        PreviewText = services.Printer.Preview(preview);
    }

    public void RefreshSession()
    {
        _session = services.Sales.GetOpenSession();
        HasOpenSession = _session is not null;
        SessionText = _session is null
            ? "Sem sessão aberta"
            : $"{_session.Name} — aberta {_session.OpenedAt:dd/MM HH:mm}";
        CanReprint = _session is not null && services.Sales.GetLastSale(_session.Id) is not null;
    }
}
