using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// The product list in Gestão is filtered by category. These run the real view
/// model against a real database, so they cover the wiring as well as the filter.
/// </summary>
public class GestaoViewModelTests
{
    private sealed class Fixture : IDisposable
    {
        public TempDb T { get; } = new();
        public AppServices Services { get; }
        public GestaoViewModel Vm { get; }

        public Fixture()
        {
            Services = new AppServices(T.Path);
            Vm = new GestaoViewModel(Services);
            Vm.Load();
        }

        public void Select(string filterName) =>
            Vm.SelectedProductFilter = Vm.ProductFilters.Single(f => f.Name == filterName);

        public void Dispose()
        {
            Services.Dispose();
            T.Dispose();
        }
    }

    [Fact]
    public void Every_category_gets_a_chip_plus_one_for_everything()
    {
        using var f = new Fixture();

        Assert.Equal(["Todas", "Bebidas", "Comida", "Sobremesas"], f.Vm.ProductFilters.Select(x => x.Name));
    }

    [Fact]
    public void Each_chip_carries_how_many_products_are_behind_it()
    {
        using var f = new Fixture();

        Assert.Equal(14, f.Vm.ProductFilters.Single(x => x.Name == "Todas").Count);
        Assert.Equal(6, f.Vm.ProductFilters.Single(x => x.Name == "Bebidas").Count);
        Assert.Equal(5, f.Vm.ProductFilters.Single(x => x.Name == "Comida").Count);
        Assert.Equal(3, f.Vm.ProductFilters.Single(x => x.Name == "Sobremesas").Count);
    }

    [Fact]
    public void The_list_opens_on_everything()
    {
        using var f = new Fixture();

        Assert.Equal("Todas", f.Vm.SelectedProductFilter!.Name);
        Assert.Equal(14, f.Vm.ProductRows.Count);
        Assert.Equal("14 produtos", f.Vm.ProductCountText);
    }

    [Fact]
    public void Choosing_a_category_shows_only_its_products()
    {
        using var f = new Fixture();

        f.Select("Bebidas");

        Assert.Equal(6, f.Vm.ProductRows.Count);
        Assert.Equal("6 produtos", f.Vm.ProductCountText);
        Assert.Contains(f.Vm.ProductRows, r => r.Name == "Cerveja");
        Assert.DoesNotContain(f.Vm.ProductRows, r => r.Name == "Bifana");
    }

    [Fact]
    public void Going_back_to_everything_shows_everything_again()
    {
        using var f = new Fixture();

        f.Select("Comida");
        Assert.Equal(5, f.Vm.ProductRows.Count);

        f.Select("Todas");
        Assert.Equal(14, f.Vm.ProductRows.Count);
    }

    [Fact]
    public void A_new_product_starts_in_the_category_being_looked_at()
    {
        using var f = new Fixture();
        f.Select("Sobremesas");

        f.Vm.NewProductCommand.Execute(null);

        Assert.Equal("Sobremesas", f.Vm.ProductCategory!.Name);
        Assert.Contains("Sobremesas", f.Vm.StatusMessage);
    }

    [Fact]
    public void Saving_into_the_filtered_category_leaves_it_on_screen()
    {
        using var f = new Fixture();
        f.Select("Sobremesas");

        f.Vm.NewProductCommand.Execute(null);
        f.Vm.ProductName = "Arroz Doce";
        f.Vm.ProductPrice = "1,80";
        f.Vm.SaveProductCommand.Execute(null);

        Assert.Equal("Sobremesas", f.Vm.SelectedProductFilter!.Name);
        Assert.Equal(4, f.Vm.ProductRows.Count);
        Assert.Equal("Arroz Doce", f.Vm.SelectedProduct!.Name);
    }

    [Fact]
    public void Moving_a_product_to_another_category_follows_it_rather_than_losing_it()
    {
        using var f = new Fixture();
        f.Select("Bebidas");

        // Reclassify a drink as a dessert. Without the filter following, the row
        // would vanish the instant it was saved and read as lost work.
        f.Vm.SelectedProduct = f.Vm.ProductRows.Single(r => r.Name == "Café");
        f.Vm.ProductCategory = f.Vm.CategoryChoices.Single(c => c.Name == "Sobremesas");
        f.Vm.SaveProductCommand.Execute(null);

        Assert.Equal("Sobremesas", f.Vm.SelectedProductFilter!.Name);
        Assert.Equal("Café", f.Vm.SelectedProduct!.Name);
        Assert.Contains(f.Vm.ProductRows, r => r.Name == "Café");
    }

    [Fact]
    public void The_chosen_category_survives_leaving_the_screen_and_coming_back()
    {
        using var f = new Fixture();
        f.Select("Comida");

        f.Vm.Load();   // what switching tabs does

        Assert.Equal("Comida", f.Vm.SelectedProductFilter!.Name);
        Assert.Equal(5, f.Vm.ProductRows.Count);
    }

    [Fact]
    public void Deleting_a_category_takes_its_chip_with_it()
    {
        using var f = new Fixture();
        f.Vm.SelectedCategory = f.Vm.CategoryRows.Single(c => c.Name == "Comida");

        f.Vm.DeleteCategoryCommand.Execute(null);

        Assert.DoesNotContain(f.Vm.ProductFilters, x => x.Name == "Comida");
        Assert.Equal(9, f.Vm.ProductFilters.Single(x => x.Name == "Todas").Count);
    }

    [Fact]
    public void An_empty_category_says_so_rather_than_looking_broken()
    {
        using var f = new Fixture();
        f.Vm.NewCategoryCommand.Execute(null);
        f.Vm.CategoryName = "Tabaco";
        f.Vm.SaveCategoryCommand.Execute(null);

        f.Select("Tabaco");

        Assert.Empty(f.Vm.ProductRows);
        Assert.Equal("sem produtos", f.Vm.ProductCountText);
    }

    [Fact]
    public void The_chips_carry_their_category_colour()
    {
        // Also proves Avalonia brushes can be built without a UI running, which is
        // what makes testing this view model possible at all.
        using var f = new Fixture();

        Assert.All(f.Vm.ProductFilters, x => Assert.NotNull(x.Background));
    }
}
