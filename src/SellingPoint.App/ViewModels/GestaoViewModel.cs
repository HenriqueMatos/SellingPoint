using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SellingPoint.Core;

namespace SellingPoint.App.ViewModels;

/// <summary>
/// One chip above the product list. A null <see cref="Category"/> is the "Todas"
/// chip, which is why the type carries the category rather than just its id.
/// </summary>
public sealed class ProductFilterViewModel(Category? category, int count)
{
    public Category? Category { get; } = category;
    public string Name => Category?.Name ?? "Todas";
    public int Count { get; } = count;

    public IBrush Background => Category is null
        ? new SolidColorBrush(Color.Parse("#3A4560"))
        : CategoryPalette.Gradient(Category.Color, 0.72);

    public bool Matches(Product product) => Category is null || product.CategoryId == Category.Id;
}

public sealed class CategoryRowViewModel(Category category)
{
    public Category Category { get; } = category;
    public string Name => Category.Name;
    public string Detail => Category.SlipMode == SlipMode.PerUnit
        ? $"{Category.PrintGroup} · uma senha por unidade"
        : $"{Category.PrintGroup} · lista agrupada";
}

public sealed class ProductRowViewModel(Product product, Category? category)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public string PriceText => Money.Format(Product.PriceCents);
    public double Opacity => Product.IsActive ? 1.0 : 0.45;

    public string Detail
    {
        get
        {
            var parts = new List<string> { category?.Name ?? "sem categoria" };
            if (Product.TrackStock) parts.Add($"stock {Product.StockQty}");
            if (!Product.IsActive) parts.Add("inativo");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Master-detail rather than an editable grid: the fields are large enough to hit
/// with a finger on the same machine that runs the till.
/// </summary>
public partial class GestaoViewModel(AppServices services) : ViewModelBase
{
    private List<Category> _categories = [];
    private List<Product> _products = [];

    public ObservableCollection<CategoryRowViewModel> CategoryRows { get; } = [];
    public ObservableCollection<ProductRowViewModel> ProductRows { get; } = [];
    public ObservableCollection<string> PrintGroups { get; } = [];
    public ObservableCollection<CategoryRowViewModel> CategoryChoices { get; } = [];
    public ObservableCollection<ProductFilterViewModel> ProductFilters { get; } = [];

    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    /// <summary>Which category's products are on screen. Null only before the first load.</summary>
    [ObservableProperty] public partial ProductFilterViewModel? SelectedProductFilter { get; set; }

    /// <summary>"6 produtos" - with the list filtered, the total stops being obvious.</summary>
    [ObservableProperty] public partial string ProductCountText { get; set; } = "";

    // --- category form -------------------------------------------------------
    [ObservableProperty] public partial CategoryRowViewModel? SelectedCategory { get; set; }
    [ObservableProperty] public partial string CategoryName { get; set; } = "";
    [ObservableProperty] public partial string CategoryColor { get; set; } = "#3A7BD5";
    [ObservableProperty] public partial string CategoryPrintGroup { get; set; } = "Bar";
    [ObservableProperty] public partial bool CategoryPerUnit { get; set; }

    // --- product form --------------------------------------------------------
    [ObservableProperty] public partial ProductRowViewModel? SelectedProduct { get; set; }
    [ObservableProperty] public partial string ProductName { get; set; } = "";
    [ObservableProperty] public partial string ProductPrice { get; set; } = "";
    [ObservableProperty] public partial CategoryRowViewModel? ProductCategory { get; set; }
    [ObservableProperty] public partial bool ProductActive { get; set; } = true;
    [ObservableProperty] public partial bool ProductTrackStock { get; set; }
    [ObservableProperty] public partial string ProductStock { get; set; } = "0";

    public void Load()
    {
        _categories = services.Catalog.GetCategories();
        _products = services.Catalog.GetProducts(activeOnly: false);

        var selectedCategoryId = SelectedCategory?.Category.Id;
        var selectedProductId = SelectedProduct?.Product.Id;

        CategoryRows.Clear();
        CategoryChoices.Clear();
        foreach (var category in _categories)
        {
            CategoryRows.Add(new CategoryRowViewModel(category));
            CategoryChoices.Add(new CategoryRowViewModel(category));
        }

        PrintGroups.Clear();
        foreach (var group in services.Catalog.GetPrintGroups()) PrintGroups.Add(group);

        // Rebuilt on every load so the counts stay honest and a deleted category
        // cannot leave a chip behind pointing at nothing.
        var previousFilterId = SelectedProductFilter?.Category?.Id;
        ProductFilters.Clear();
        ProductFilters.Add(new ProductFilterViewModel(null, _products.Count));
        foreach (var category in _categories)
            ProductFilters.Add(new ProductFilterViewModel(category, _products.Count(p => p.CategoryId == category.Id)));

        SelectedProductFilter = ProductFilters.FirstOrDefault(f => f.Category?.Id == previousFilterId)
                                ?? ProductFilters[0];

        RefreshProductRows(selectedProductId);

        // Falling back to the first row means the form arrives filled in rather
        // than blank, which reads as "nothing here" on a screen full of nothing.
        SelectedCategory = CategoryRows.FirstOrDefault(r => r.Category.Id == selectedCategoryId)
                           ?? CategoryRows.FirstOrDefault();
    }

    partial void OnSelectedProductFilterChanged(ProductFilterViewModel? value) => RefreshProductRows();

    /// <summary>
    /// Rebuilds the product list for the active filter, keeping the selected
    /// product if it is still on screen.
    /// </summary>
    private void RefreshProductRows(int? keepProductId = null)
    {
        keepProductId ??= SelectedProduct?.Product.Id;

        var filter = SelectedProductFilter;
        var byId = _categories.ToDictionary(c => c.Id);

        ProductRows.Clear();
        foreach (var product in _products.Where(p => filter is null || filter.Matches(p)))
            ProductRows.Add(new ProductRowViewModel(product, byId.GetValueOrDefault(product.CategoryId)));

        ProductCountText = ProductRows.Count switch
        {
            0 => "sem produtos",
            1 => "1 produto",
            var n => $"{n} produtos"
        };

        SelectedProduct = ProductRows.FirstOrDefault(r => r.Product.Id == keepProductId)
                          ?? ProductRows.FirstOrDefault();
    }

    partial void OnSelectedCategoryChanged(CategoryRowViewModel? value)
    {
        if (value is null) return;

        CategoryName = value.Category.Name;
        CategoryColor = value.Category.Color;
        CategoryPrintGroup = value.Category.PrintGroup;
        CategoryPerUnit = value.Category.SlipMode == SlipMode.PerUnit;
    }

    partial void OnSelectedProductChanged(ProductRowViewModel? value)
    {
        if (value is null) return;

        ProductName = value.Product.Name;
        ProductPrice = Money.FormatPlain(value.Product.PriceCents);
        ProductCategory = CategoryChoices.FirstOrDefault(c => c.Category.Id == value.Product.CategoryId);
        ProductActive = value.Product.IsActive;
        ProductTrackStock = value.Product.TrackStock;
        ProductStock = value.Product.StockQty.ToString();
    }

    // --- categories ----------------------------------------------------------

    [RelayCommand]
    private void NewCategory()
    {
        SelectedCategory = null;
        CategoryName = "";
        CategoryColor = "#3A7BD5";
        CategoryPrintGroup = PrintGroups.FirstOrDefault() ?? "Bar";
        CategoryPerUnit = false;
        StatusMessage = "Nova categoria — preencha e guarde.";
    }

    [RelayCommand]
    private void SaveCategory()
    {
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            StatusMessage = "A categoria precisa de um nome.";
            return;
        }

        var category = SelectedCategory?.Category ?? new Category { SortOrder = _categories.Count };
        category.Name = CategoryName.Trim();
        category.Color = string.IsNullOrWhiteSpace(CategoryColor) ? "#3A7BD5" : CategoryColor.Trim();
        category.PrintGroup = string.IsNullOrWhiteSpace(CategoryPrintGroup) ? "Bar" : CategoryPrintGroup.Trim();
        category.SlipMode = CategoryPerUnit ? SlipMode.PerUnit : SlipMode.Grouped;

        if (category.Id == 0) services.Catalog.InsertCategory(category);
        else services.Catalog.UpdateCategory(category);

        var id = category.Id;
        Load();
        SelectedCategory = CategoryRows.FirstOrDefault(r => r.Category.Id == id);
        StatusMessage = $"Categoria '{category.Name}' guardada.";
    }

    [RelayCommand]
    private void DeleteCategory()
    {
        if (SelectedCategory is not { } row) return;

        var count = _products.Count(p => p.CategoryId == row.Category.Id);
        services.Catalog.DeleteCategory(row.Category.Id);

        SelectedCategory = null;
        Load();
        StatusMessage = count > 0
            ? $"Categoria apagada, com os seus {count} produto(s)."
            : "Categoria apagada.";
    }

    [RelayCommand] private void MoveCategoryUp() => MoveCategory(-1);
    [RelayCommand] private void MoveCategoryDown() => MoveCategory(1);

    private void MoveCategory(int direction)
    {
        if (SelectedCategory is not { } row) return;

        var index = _categories.FindIndex(c => c.Id == row.Category.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _categories.Count) return;

        Swap(_categories[index], _categories[target]);
        services.Catalog.UpdateCategory(_categories[index]);
        services.Catalog.UpdateCategory(_categories[target]);

        var id = row.Category.Id;
        Load();
        SelectedCategory = CategoryRows.FirstOrDefault(r => r.Category.Id == id);
    }

    // --- products ------------------------------------------------------------

    [RelayCommand]
    private void NewProduct()
    {
        // The filtered category wins: standing in Bebidas and pressing Novo should
        // start a drink, not whatever happened to be selected before.
        var filtered = SelectedProductFilter?.Category;
        var category = (filtered is null ? null : CategoryChoices.FirstOrDefault(c => c.Category.Id == filtered.Id))
                       ?? ProductCategory
                       ?? CategoryChoices.FirstOrDefault();

        SelectedProduct = null;
        ProductName = "";
        ProductPrice = "";
        ProductCategory = category;
        ProductActive = true;
        ProductTrackStock = false;
        ProductStock = "0";

        StatusMessage = category is null
            ? "Crie primeiro uma categoria."
            : $"Novo produto em {category.Name} — preencha e guarde.";
    }

    [RelayCommand]
    private void SaveProduct()
    {
        if (string.IsNullOrWhiteSpace(ProductName))
        {
            StatusMessage = "O produto precisa de um nome.";
            return;
        }

        if (ProductCategory is not { } category)
        {
            StatusMessage = "Escolha uma categoria.";
            return;
        }

        if (!Money.TryParseEuros(ProductPrice, out var priceCents) || priceCents < 0)
        {
            StatusMessage = "Preço inválido. Escreva por exemplo 1,50.";
            return;
        }

        var product = SelectedProduct?.Product ?? new Product { SortOrder = _products.Count };
        product.Name = ProductName.Trim();
        product.PriceCents = priceCents;
        product.CategoryId = category.Category.Id;
        product.IsActive = ProductActive;
        product.TrackStock = ProductTrackStock;
        product.StockQty = int.TryParse(ProductStock, out var stock) ? stock : 0;

        if (product.Id == 0) services.Catalog.InsertProduct(product);
        else services.Catalog.UpdateProduct(product);

        var id = product.Id;
        Load();

        // Moving a product to another category would otherwise drop it out of the
        // filtered list the instant it was saved, which reads as losing the thing
        // you just typed. Follow it instead.
        if (ProductRows.All(r => r.Product.Id != id))
        {
            SelectedProductFilter = ProductFilters.FirstOrDefault(f => f.Category?.Id == product.CategoryId)
                                    ?? ProductFilters[0];
        }

        SelectedProduct = ProductRows.FirstOrDefault(r => r.Product.Id == id);
        StatusMessage = $"'{product.Name}' guardado em {category.Name} a {Money.Format(priceCents)}.";
    }

    [RelayCommand]
    private void DeleteProduct()
    {
        if (SelectedProduct is not { } row) return;

        services.Catalog.DeleteProduct(row.Product.Id);
        SelectedProduct = null;
        Load();
        StatusMessage = "Produto apagado. As vendas antigas mantêm o nome e o preço que tinham.";
    }

    [RelayCommand] private void MoveProductUp() => MoveProduct(-1);
    [RelayCommand] private void MoveProductDown() => MoveProduct(1);

    private void MoveProduct(int direction)
    {
        if (SelectedProduct is not { } row) return;

        // Only reorder within the product's own category - that is the order the
        // till shows, and moving past a category boundary would look like nothing
        // happened.
        var siblings = _products.Where(p => p.CategoryId == row.Product.CategoryId).ToList();
        var index = siblings.FindIndex(p => p.Id == row.Product.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= siblings.Count) return;

        Swap(siblings[index], siblings[target]);
        services.Catalog.UpdateProduct(siblings[index]);
        services.Catalog.UpdateProduct(siblings[target]);

        var id = row.Product.Id;
        Load();
        SelectedProduct = ProductRows.FirstOrDefault(r => r.Product.Id == id);
    }

    private static void Swap(Category a, Category b) => (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
    private static void Swap(Product a, Product b) => (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
}
