using Dapper;
using SellingPoint.Core;

namespace SellingPoint.Data;

/// <summary>Categories and products - everything the Gestao screen edits.</summary>
public sealed class CatalogRepository(Db db)
{
    public List<Category> GetCategories()
    {
        using var c = db.Open();
        return c.Query<Category>("SELECT * FROM category ORDER BY sort_order, id").AsList();
    }

    public List<Product> GetProducts(bool activeOnly = true)
    {
        using var c = db.Open();
        var where = activeOnly ? "WHERE is_active = 1" : "";
        return c.Query<Product>($"SELECT * FROM product {where} ORDER BY sort_order, id").AsList();
    }

    /// <summary>Existing values, to offer as a dropdown rather than making the
    /// organizer retype "Cozinha" and wonder why it printed separately.</summary>
    public List<string> GetPrintGroups()
    {
        using var c = db.Open();
        return c.Query<string>("SELECT DISTINCT print_group FROM category ORDER BY print_group").AsList();
    }

    public int InsertCategory(Category category)
    {
        using var c = db.Open();
        return category.Id = c.ExecuteScalar<int>(
            """
            INSERT INTO category(name, color, sort_order, print_group, slip_mode)
            VALUES(@Name, @Color, @SortOrder, @PrintGroup, @SlipMode);
            SELECT last_insert_rowid();
            """,
            Params(category));
    }

    public void UpdateCategory(Category category)
    {
        using var c = db.Open();
        c.Execute(
            """
            UPDATE category SET name = @Name, color = @Color, sort_order = @SortOrder,
                                print_group = @PrintGroup, slip_mode = @SlipMode
            WHERE id = @Id
            """,
            Params(category));
    }

    /// <summary>Cascades to the category's products - the schema says so.</summary>
    public void DeleteCategory(int id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM category WHERE id = @id", new { id });
    }

    public int InsertProduct(Product product)
    {
        using var c = db.Open();
        return product.Id = c.ExecuteScalar<int>(
            """
            INSERT INTO product(category_id, name, price_cents, sort_order, is_active, track_stock, stock_qty)
            VALUES(@CategoryId, @Name, @PriceCents, @SortOrder, @IsActive, @TrackStock, @StockQty);
            SELECT last_insert_rowid();
            """,
            product);
    }

    public void UpdateProduct(Product product)
    {
        using var c = db.Open();
        c.Execute(
            """
            UPDATE product SET category_id = @CategoryId, name = @Name, price_cents = @PriceCents,
                               sort_order = @SortOrder, is_active = @IsActive,
                               track_stock = @TrackStock, stock_qty = @StockQty
            WHERE id = @Id
            """,
            product);
    }

    public void DeleteProduct(int id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM product WHERE id = @id", new { id });
    }

    /// <summary>A restock or a correction. Moves the stock and logs why.</summary>
    public void AdjustStock(int productId, int delta, string reason, DateTime now, int? sessionId = null)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("UPDATE product SET stock_qty = stock_qty + @delta WHERE id = @productId",
            new { delta, productId }, tx);
        c.Execute(
            """
            INSERT INTO stock_adjustment(product_id, delta, reason, created_at, session_id)
            VALUES(@productId, @delta, @reason, @now, @sessionId)
            """,
            new { productId, delta, reason, now, sessionId }, tx);

        tx.Commit();
    }

    // slip_mode is stored as readable text ("Grouped"), so the database can be
    // understood by anyone who opens it without a copy of the enum to hand.
    private static object Params(Category category) => new
    {
        category.Id,
        category.Name,
        category.Color,
        category.SortOrder,
        category.PrintGroup,
        SlipMode = category.SlipMode.ToString()
    };
}
