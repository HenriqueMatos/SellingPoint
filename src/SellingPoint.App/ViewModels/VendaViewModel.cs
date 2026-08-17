using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;
using SellingPoint.Printing;

namespace SellingPoint.App.ViewModels;

/// <summary>The till. Press products, take money, print.</summary>
public partial class VendaViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly Cart _cart = new();
    private List<Category> _categories = [];
    private List<Product> _products = [];
    private Session? _session;

    public VendaViewModel(AppServices appServices)
    {
        services = appServices;
        Diagnostics = new PrinterDiagnosticsViewModel(appServices);

        services.Print.Changed += OnPrintServiceChanged;
        RefreshPrinterChip();
    }

    public PrinterDiagnosticsViewModel Diagnostics { get; }

    public ObservableCollection<CategoryTabViewModel> Categories { get; } = [];
    public ObservableCollection<ProductButtonViewModel> Products { get; } = [];
    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];
    public ObservableCollection<QuickTenderViewModel> QuickTenders { get; } = [];

    [ObservableProperty] public partial CategoryTabViewModel? SelectedCategory { get; set; }
    [ObservableProperty] public partial string TotalText { get; set; } = Money.Format(0);
    [ObservableProperty] public partial string CartCountText { get; set; } = "";
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

    [ObservableProperty] public partial bool IsDiagnosticsOpen { get; set; }
    [ObservableProperty] public partial string PrinterChipText { get; set; } = "";
    [ObservableProperty] public partial IBrush PrinterChipBrush { get; set; } = Brushes.Gray;
    [ObservableProperty] public partial bool PrinterNeedsAttention { get; set; }

    public int CashReceivedCents => int.TryParse(CashEntry, out var cents) ? cents : 0;
    public string CashReceivedText => Money.Format(CashReceivedCents);
    public string ChangeText => Tender.TryChange(_cart.TotalCents, CashReceivedCents, out var change)
        ? Money.Format(change)
        : "--";
    public bool CanConfirmCash => !_cart.IsEmpty && CashReceivedCents >= _cart.TotalCents;

    /// <summary>
    /// Paying needs both a cart and an open session. Without this the buttons look
    /// live, do nothing when pressed, and leave the operator tapping harder.
    /// </summary>
    public bool CanPay => !IsCartEmpty && HasOpenSession;

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

    // Looking up a slip somebody is holding. "The kitchen never served number 87"
    // could not be answered at all before: reprint only ever reached the last
    // sale, so the volunteer had to refuse someone who might be right or hand over
    // food to someone who was not.
    [ObservableProperty] public partial bool IsTicketSearchOpen { get; set; }
    [ObservableProperty] public partial string TicketSearchEntry { get; set; } = "";
    [ObservableProperty] public partial string TicketSearchResult { get; set; } = "";
    [ObservableProperty] public partial bool HasFoundTicket { get; set; }

    private Sale? _foundSale;

    [RelayCommand]
    private void OpenTicketSearch()
    {
        TicketSearchEntry = "";
        TicketSearchResult = "";
        HasFoundTicket = false;
        _foundSale = null;
        IsTicketSearchOpen = true;
    }

    [RelayCommand]
    private void CloseTicketSearch() => IsTicketSearchOpen = false;

    [RelayCommand]
    private void TicketSearchDigit(string digit)
        => TicketSearchEntry = (TicketSearchEntry + digit).TrimStart('0');

    [RelayCommand]
    private void TicketSearchClear()
    {
        TicketSearchEntry = "";
        TicketSearchResult = "";
        HasFoundTicket = false;
        _foundSale = null;
    }

    [RelayCommand]
    private void FindTicket()
    {
        HasFoundTicket = false;
        _foundSale = null;

        if (_session is null)
        {
            TicketSearchResult = "Abra uma sessão primeiro.";
            return;
        }

        if (!int.TryParse(TicketSearchEntry, out var number) || number <= 0)
        {
            TicketSearchResult = "Escreva o número que está no talão, por exemplo 87.";
            return;
        }

        // Only this session: ticket numbers restart at 1 with each one, so a
        // number alone does not identify a sale across the whole database.
        var sale = services.Sales.GetSaleByTicket(_session.Id, number);
        if (sale is null)
        {
            TicketSearchResult = $"Não há nenhum talão {TicketBuilder.Reference(number)} nesta sessão.";
            return;
        }

        _foundSale = sale;
        HasFoundTicket = true;
        TicketSearchResult = Describe(sale);
    }

    /// <summary>What was on the slip, in the order it was rung up.</summary>
    private static string Describe(Sale sale)
    {
        var lines = sale.Lines.Select(l => $"{l.Qty}x {l.ProductName}   {Money.Format(l.LineTotalCents)}");
        var method = sale.PaymentMethod == PaymentMethod.Cash ? "dinheiro" : "cartão";

        return $"{TicketBuilder.Reference(sale.TicketNumber)} — {sale.CreatedAt:HH:mm}, {method}\n"
               + string.Join('\n', lines)
               + $"\nTOTAL   {Money.Format(sale.TotalCents)}";
    }

    [RelayCommand]
    private void ReprintFound()
    {
        if (_foundSale is not { } sale) return;

        var slips = services.Print.Enqueue(sale);
        IsTicketSearchOpen = false;
        StatusMessage = $"Talão {TicketBuilder.Reference(sale.TicketNumber)} reimpresso — {slips} senha(s).";
    }

    [RelayCommand]
    private void ConfirmCash() => Complete(PaymentMethod.Cash, CashReceivedCents);

    [RelayCommand]
    private void PayCard() => Complete(PaymentMethod.Card, 0);

    [RelayCommand]
    private void ReprintLast()
    {
        if (_session is null) return;

        var last = services.Sales.GetLastSale(_session.Id);
        if (last is null)
        {
            StatusMessage = "Ainda não há vendas nesta sessão.";
            return;
        }

        services.Print.Enqueue(last);
        StatusMessage = $"Talão {TicketBuilder.Reference(last.TicketNumber)} enviado outra vez.";
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        Diagnostics.Disarm();
        Diagnostics.Refresh();
        IsDiagnosticsOpen = true;
    }

    [RelayCommand]
    private void CloseDiagnostics() => IsDiagnosticsOpen = false;

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewVisible = !IsPreviewVisible;
        RefreshPreview();
    }

    // Which festival tonight belongs to. Asked once per festival, not once per
    // night: with one already running the night joins it and nobody is asked.
    [ObservableProperty] public partial string EventNameEntry { get; set; } = "";
    [ObservableProperty] public partial bool NeedsEvent { get; set; }
    [ObservableProperty] public partial string EventText { get; set; } = "";

    /// <summary>
    /// Ticked to start a festival rather than join the one still open.
    ///
    /// This has to be offered, not assumed. Closing the last night of a festival
    /// does not close the festival - it cannot, since nobody knows it was the last
    /// - so a festival stays open until somebody says otherwise. Without a way to
    /// say it, next August's first night would silently join last August's, and
    /// every sale of the new festival would be reported under the old one.
    /// </summary>
    [ObservableProperty] public partial bool StartNewEvent { get; set; }

    public bool AsksForEventName => NeedsEvent || StartNewEvent;

    partial void OnNeedsEventChanged(bool value) => OnPropertyChanged(nameof(AsksForEventName));

    partial void OnStartNewEventChanged(bool value)
    {
        OnPropertyChanged(nameof(AsksForEventName));
        if (value) EventNameEntry = $"Festa {DateTime.Now:yyyy}";
    }

    [RelayCommand]
    private void OpenSessionPanel()
    {
        SessionNameEntry = $"Sessão {DateTime.Now:dd/MM/yyyy}";
        SessionFloatEntry = "";
        StartNewEvent = false;

        var festival = services.Sales.GetOpenEvent();
        NeedsEvent = festival is null;
        EventNameEntry = festival?.Name ?? $"Festa {DateTime.Now:yyyy}";

        // The festival is named on screen even when nothing is being asked, so an
        // old one still open is seen rather than joined by accident.
        EventText = festival is null
            ? ""
            : $"Esta noite entra em «{festival.Name}», aberta em {festival.CreatedAt:dd/MM/yyyy}.";

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

        if (AsksForEventName && string.IsNullOrWhiteSpace(EventNameEntry))
        {
            StatusMessage = "Dê um nome à festa.";
            return;
        }

        // An empty float is a legitimate answer, not an error.
        if (!Money.TryParseEuros(SessionFloatEntry, out var floatCents)) floatCents = 0;

        // The repository refuses a second open session, and the button that gets
        // here is hidden while one is open - but a stale screen could still ask,
        // and an exception out of a command handler takes the whole till with it.
        try
        {
            Event? festival;

            if (AsksForEventName)
            {
                // Starting one ends the one before it. Its nights are all closed -
                // only one session can be open, and this is opening it - so there
                // is nothing left to count in it.
                if (services.Sales.GetOpenEvent() is { } previous)
                    services.Sales.CloseEvent(previous.Id, DateTime.Now);

                festival = services.Sales.OpenEvent(EventNameEntry.Trim(), DateTime.Now);
            }
            else festival = services.Sales.GetOpenEvent();

            services.Sales.OpenSession(SessionNameEntry.Trim(), floatCents, DateTime.Now, festival?.Id);
        }
        catch (InvalidOperationException e)
        {
            StatusMessage = e.Message;
            RefreshSession();
            return;
        }

        IsSessionPanelOpen = false;
        RefreshSession();
        StatusMessage = $"Sessão aberta com {Money.Format(floatCents)} de fundo de caixa.";
    }

    private void Complete(PaymentMethod method, int cashReceivedCents)
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

        // Queued rather than printed. A printer that is out of paper, unplugged, or
        // has been given a new COM number by Windows delays the ticket instead of
        // losing it - the queue drains itself the moment one answers again.
        var slips = services.Print.Enqueue(sale);
        var waiting = services.Print.PendingCount > slips
            ? $" {services.Print.PendingCount} talões à espera da impressora."
            : "";

        StatusMessage = $"Talão {reference} — {slips} senha(s).{change}{waiting}";

        _cart.Clear();
        IsCashPanelOpen = false;
        CashEntry = "";
        CanReprint = true;

        // Stock moved, so the buttons need their counts back.
        _products = services.Catalog.GetProducts();
        RefreshProducts();
        RefreshCart();
    }

    private void OnPrintServiceChanged() => Dispatcher.UIThread.Post(RefreshPrinterChip);

    /// <summary>
    /// The one indicator on the till that says whether paper is coming out. Red is
    /// meant to be noticed from across a counter.
    /// </summary>
    private void RefreshPrinterChip()
    {
        var print = services.Print;
        var status = print.Status;
        var pending = print.PendingCount;

        PrinterNeedsAttention = !status.CanPrint || (pending > 0 && print.LastError is not null);

        PrinterChipBrush = status.State switch
        {
            _ when print.IsPaused => Brushes.SlateGray,
            PrinterState.Ready or PrinterState.Unknown => Brushes.MediumSeaGreen,
            PrinterState.PaperLow => Brushes.Goldenrod,
            _ => Brushes.IndianRed
        };

        var queue = pending switch
        {
            0 => "",
            1 => " · 1 à espera",
            _ => $" · {pending} à espera"
        };

        PrinterChipText = (print.IsPaused ? "Impressão em pausa" : status.Message) + queue;
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
        OnPropertyChanged(nameof(CanPay));
        CartCountText = _cart.ItemCount switch
        {
            0 => "",
            1 => "1 artigo",
            var n => $"{n} artigos"
        };

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
        OnPropertyChanged(nameof(CanPay));
    }
}
