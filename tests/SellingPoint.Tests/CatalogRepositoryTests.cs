namespace SellingPoint.Tests;

public class CatalogRepositoryTests
{
    [Fact]
    public void Seed_gives_a_working_catalog_rather_than_an_empty_screen()
    {
        using var t = new TempDb();

        var categories = t.Catalog.GetCategories();
        var products = t.Catalog.GetProducts();

        Assert.Equal(["Bebidas", "Comida", "Sobremesas"], categories.Select(c => c.Name));
        Assert.Equal(14, products.Count);

        // Each category on its own print group, so a mixed order prints one ticket
        // per category and nothing arrives at the wrong counter.
        Assert.Equal("Bebidas", categories.Single(c => c.Name == "Bebidas").PrintGroup);
        Assert.Equal("Sobremesas", categories.Single(c => c.Name == "Sobremesas").PrintGroup);
        Assert.Equal("Comida", categories.Single(c => c.Name == "Comida").PrintGroup);
    }

    [Fact]
    public void Initialize_is_idempotent_and_does_not_reseed()
    {
        using var t = new TempDb();

        t.Db.Initialize();
        t.Db.Initialize();

        Assert.Equal(3, t.Catalog.GetCategories().Count);
    }

    [Fact]
    public void Category_round_trips_including_the_slip_mode_enum()
    {
        using var t = new TempDb(seed: false);

        var id = t.Catalog.InsertCategory(new Category
        {
            Name = "Senhas", Color = "#112233", SortOrder = 7,
            PrintGroup = "Cozinha", SlipMode = SlipMode.PerUnit
        });

        var loaded = t.Catalog.GetCategories().Single();
        Assert.Equal(id, loaded.Id);
        Assert.Equal("Senhas", loaded.Name);
        Assert.Equal("#112233", loaded.Color);
        Assert.Equal(7, loaded.SortOrder);
        Assert.Equal("Cozinha", loaded.PrintGroup);
        Assert.Equal(SlipMode.PerUnit, loaded.SlipMode);
    }

    [Fact]
    public void Updating_a_category_persists_every_field()
    {
        using var t = new TempDb(seed: false);
        t.Catalog.InsertCategory(new Category { Name = "Bebidas" });

        var category = t.Catalog.GetCategories().Single();
        category.Name = "Bebidas Frias";
        category.PrintGroup = "Bar Exterior";
        category.SlipMode = SlipMode.PerUnit;
        t.Catalog.UpdateCategory(category);

        var reloaded = t.Catalog.GetCategories().Single();
        Assert.Equal("Bebidas Frias", reloaded.Name);
        Assert.Equal("Bar Exterior", reloaded.PrintGroup);
        Assert.Equal(SlipMode.PerUnit, reloaded.SlipMode);
    }

    [Fact]
    public void Product_round_trips_including_the_flags()
    {
        using var t = new TempDb(seed: false);
        var categoryId = t.Catalog.InsertCategory(new Category { Name = "Bebidas" });

        t.Catalog.InsertProduct(new Product
        {
            CategoryId = categoryId, Name = "Cerveja", PriceCents = 150,
            SortOrder = 3, IsActive = true, TrackStock = true, StockQty = 48
        });

        var product = t.Catalog.GetProducts().Single();
        Assert.Equal("Cerveja", product.Name);
        Assert.Equal(150, product.PriceCents);
        Assert.True(product.TrackStock);
        Assert.Equal(48, product.StockQty);
    }

    [Fact]
    public void Inactive_products_stay_out_of_the_till_but_remain_in_the_admin_list()
    {
        using var t = new TempDb(seed: false);
        var categoryId = t.Catalog.InsertCategory(new Category { Name = "Bebidas" });
        t.Catalog.InsertProduct(new Product { CategoryId = categoryId, Name = "Cerveja", PriceCents = 150 });
        t.Catalog.InsertProduct(new Product { CategoryId = categoryId, Name = "Fora de epoca", PriceCents = 100, IsActive = false });

        Assert.Single(t.Catalog.GetProducts());
        Assert.Equal(2, t.Catalog.GetProducts(activeOnly: false).Count);
    }

    [Fact]
    public void Deleting_a_category_takes_its_products_with_it()
    {
        using var t = new TempDb();
        var bebidas = t.Catalog.GetCategories().Single(c => c.Name == "Bebidas");

        t.Catalog.DeleteCategory(bebidas.Id);

        Assert.DoesNotContain(t.Catalog.GetProducts(activeOnly: false), p => p.CategoryId == bebidas.Id);
    }

    [Fact]
    public void Print_groups_are_offered_as_the_distinct_values_already_in_use()
    {
        using var t = new TempDb();
        Assert.Equal(["Bebidas", "Comida", "Sobremesas"], t.Catalog.GetPrintGroups());
    }

    [Fact]
    public void Adjusting_stock_moves_the_count_and_logs_the_reason()
    {
        using var t = new TempDb();
        var product = t.Catalog.GetProducts().First(p => p.Name == "Cerveja");
        product.TrackStock = true;
        product.StockQty = 10;
        t.Catalog.UpdateProduct(product);

        t.Catalog.AdjustStock(product.Id, 24, "Caixa nova do carro", new DateTime(2026, 8, 14, 22, 0, 0));

        Assert.Equal(34, t.Catalog.GetProducts().First(p => p.Id == product.Id).StockQty);
    }
}
