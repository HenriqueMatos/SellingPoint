using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using SellingPoint.Core;

namespace SellingPoint.Data;

/// <summary>
/// Owns the SQLite file: where it lives, how connections are opened, and applying
/// the schema. One file, one machine, no server.
/// </summary>
public sealed class Db(string path)
{
    static Db() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public string Path { get; } = path;

    /// <summary>%APPDATA%\SellingPoint\sellingpoint.db on Windows, ~/.config on macOS.</summary>
    public static string DefaultPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SellingPoint");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "sellingpoint.db");
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={Path}");
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }

    public void Initialize(bool seedIfEmpty = true)
    {
        using var connection = Open();
        connection.Execute(ReadSchema());

        if (seedIfEmpty && connection.ExecuteScalar<long>("SELECT COUNT(*) FROM category") == 0)
            Seed(connection);
    }

    /// <summary>Timestamped copy of the whole database. Called on session close and
    /// from Settings. An event laptop that dies at midnight should not take the
    /// night's takings with it.</summary>
    public string Backup(DateTime now)
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        var target = System.IO.Path.Combine(dir, $"backup-{now:yyyyMMdd-HHmmss}.db");

        using (var source = Open())
        using (var destination = new SqliteConnection($"Data Source={target}"))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        return target;
    }

    private static string ReadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("schema.sql", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// A worked example rather than an empty screen. Each category gets its own
    /// print group, so an order produces one ticket per category: the customer
    /// hands the drinks slip to the bar and the food slip to the kitchen, and
    /// nothing arrives at the wrong counter.
    ///
    /// Combining is the thing you opt into: give two categories the same group
    /// name in Gestao and they share a slip.
    /// </summary>
    private static void Seed(SqliteConnection connection)
    {
        var catalog = new (string Name, string Color, string PrintGroup, SlipMode Mode, (string Name, int Cents)[] Products)[]
        {
            ("Bebidas", "#2563EB", "Bebidas", SlipMode.Grouped,
            [
                ("Cerveja", 150), ("Refrigerante", 120), ("Sumo", 100),
                ("Água", 80), ("Vinho", 150), ("Café", 70)
            ]),
            ("Comida", "#EA580C", "Comida", SlipMode.Grouped,
            [
                ("Bifana", 300), ("Cachorro", 250), ("Hambúrguer", 350),
                ("Sandes de Leitão", 400), ("Batatas Fritas", 200)
            ]),
            ("Sobremesas", "#DB2777", "Sobremesas", SlipMode.Grouped,
            [
                ("Bolo", 150), ("Farturas", 200), ("Gelado", 150)
            ])
        };

        var categoryOrder = 0;
        foreach (var (name, color, printGroup, mode, products) in catalog)
        {
            var categoryId = connection.ExecuteScalar<int>(
                """
                INSERT INTO category(name, color, sort_order, print_group, slip_mode)
                VALUES(@name, @color, @sortOrder, @printGroup, @slipMode);
                SELECT last_insert_rowid();
                """,
                new { name, color, sortOrder = categoryOrder++, printGroup, slipMode = mode.ToString() });

            var productOrder = 0;
            foreach (var (productName, cents) in products)
            {
                connection.Execute(
                    """
                    INSERT INTO product(category_id, name, price_cents, sort_order, is_active, track_stock, stock_qty)
                    VALUES(@categoryId, @productName, @cents, @sortOrder, 1, 0, 0);
                    """,
                    new { categoryId, productName, cents, sortOrder = productOrder++ });
            }
        }
    }
}
