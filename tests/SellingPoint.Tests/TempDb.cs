using Microsoft.Data.Sqlite;

namespace SellingPoint.Tests;

/// <summary>
/// A real SQLite file in the temp directory, thrown away afterwards. Chosen over
/// :memory: because an in-memory database is per-connection, and these repositories
/// deliberately open a connection per call.
/// </summary>
public sealed class TempDb : IDisposable
{
    public Db Db { get; }
    public string Path { get; }

    public CatalogRepository Catalog { get; }
    public SalesRepository Sales { get; }
    public SettingsRepository Settings { get; }
    public PrintQueueRepository PrintQueue { get; }

    public TempDb(bool seed = true)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sellingpoint-test-{Guid.NewGuid():N}.db");
        Db = new Db(Path);
        Db.Initialize(seedIfEmpty: seed);

        Catalog = new CatalogRepository(Db);
        Sales = new SalesRepository(Db);
        Settings = new SettingsRepository(Db);
        PrintQueue = new PrintQueueRepository(Db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!,
                     System.IO.Path.GetFileNameWithoutExtension(Path) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { /* the OS will get it */ }
        }
    }
}
