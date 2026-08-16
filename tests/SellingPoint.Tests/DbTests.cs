using Dapper;

namespace SellingPoint.Tests;

public class DbTests
{
    [Fact]
    public void The_database_writes_ahead_rather_than_journalling_every_transaction()
    {
        // SQLite's default creates a journal file, syncs it, writes, syncs again
        // and deletes the journal - once per transaction. On the Windows machine
        // this runs on, creating and deleting files is also what the virus
        // scanner inspects. Write-ahead logging does away with all of it.
        using var t = new TempDb();
        using var c = t.Db.Open();

        Assert.Equal("wal", c.ExecuteScalar<string>("PRAGMA journal_mode;"));
    }

    [Fact]
    public void The_mode_survives_being_closed_and_opened_again()
    {
        // It is set once at startup because SQLite records it in the file itself.
        // If that stopped being true, every later connection would quietly fall
        // back to journalling and the setting above would be worth nothing.
        using var t = new TempDb();

        using var reopened = new Db(t.Path).Open();
        Assert.Equal("wal", reopened.ExecuteScalar<string>("PRAGMA journal_mode;"));
    }

    [Fact]
    public void Money_is_still_written_durably()
    {
        // The speed gain stops here. synchronous stays FULL: a power cut at a
        // festival is not a hypothetical, and what would be lost is sales.
        using var t = new TempDb();
        using var c = t.Db.Open();

        Assert.Equal(2, c.ExecuteScalar<int>("PRAGMA synchronous;"));
    }

    [Fact]
    public void A_backup_taken_from_a_write_ahead_database_carries_the_sales()
    {
        // The backup API reads through the write-ahead log rather than the file
        // alone. Getting this wrong would produce a backup file that opens
        // cleanly and is missing the most recent night.
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, new DateTime(2026, 8, 14, 21, 0, 0));

        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Cerveja"), 2);
        t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 300, new DateTime(2026, 8, 14, 21, 30, 0)),
            session.Id);

        var backup = t.Db.Backup(new DateTime(2026, 8, 15, 2, 0, 0));

        try
        {
            Assert.Equal(300, new ReportRepository(new Db(backup))
                .Build(new SalesRepository(new Db(backup)).GetSessions().Single()).CashCents);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(backup)!,
                         Path.GetFileNameWithoutExtension(backup) + "*"))
            {
                try { File.Delete(file); } catch (IOException) { /* the OS will get it */ }
            }
        }
    }
}
