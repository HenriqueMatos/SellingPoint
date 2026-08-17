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

        // Write-ahead logging, set once and then remembered in the file itself.
        // The default creates a journal file, syncs it, writes, syncs again and
        // deletes the journal - for every transaction. On Windows, creating and
        // deleting files is also exactly what the virus scanner watches.
        //
        // synchronous stays at its FULL default on purpose. NORMAL would be
        // faster still, but it can lose the last transactions to a power cut, and
        // these are sales. A festival runs off extension leads.
        connection.Execute("PRAGMA journal_mode = WAL;");

        connection.Execute(ReadSchema());
        Migrate(connection);

        if (seedIfEmpty && connection.ExecuteScalar<long>("SELECT COUNT(*) FROM category") == 0)
            Seed(connection);
    }

    /// <summary>
    /// The changes schema.sql cannot make on a database that already exists. It is
    /// additive by CREATE IF NOT EXISTS, which covers new tables and new indexes
    /// and nothing else; a new column on a table that is already there needs this.
    ///
    /// Each step runs once. The version is only moved forward inside the step's own
    /// transaction, so a step that fails halfway leaves the database on the version
    /// it was and is tried again next time rather than half applied.
    /// </summary>
    private void Migrate(SqliteConnection connection)
    {
        // Asked of the database rather than of the version number it is stamped
        // with. The stamp is bookkeeping and can be wrong - a database whose
        // schema_version row went missing gets stamped with the current one by
        // schema.sql and would then skip a step it still needs. The column either
        // exists or it does not, and that cannot be wrong.
        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('session')").ToList();
        if (columns.Contains("event_id")) return;

        // A copy of the file as it was, before anything is changed.
        //
        // Every destructive thing the screens do takes one first, and this is the
        // only place the program rewrites the shape of a committee's live database
        // - once, unattended, at startup. If a step turns out to be wrong for data
        // nobody here has seen, the way back has to already exist.
        Backup(connection, "antes-da-migracao");

        SessionsBelongToAnEvent(connection);

        // Both paths have session.event_id by now - the one schema.sql builds fresh
        // and the one the step above alters - so this is the first point at which
        // the index can be asked for at all.
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_session_event ON session(event_id)");
    }

    /// <summary>
    /// Version 2: every session belongs to a festival.
    ///
    /// What is already in the database has to go somewhere, so the sessions of one
    /// calendar year become one event - a village festival is annual, so the guess
    /// is a good one, and the name can be corrected on screen afterwards.
    ///
    /// Deliberately done in SQL with no clock: an event's created_at is the first
    /// session it holds, which is both truer than "now" and the same answer however
    /// many times this is read.
    /// </summary>
    private static void SessionsBelongToAnEvent(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();

        connection.Execute("ALTER TABLE session ADD COLUMN event_id INTEGER REFERENCES event(id)",
            transaction: tx);
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_session_event ON session(event_id)",
            transaction: tx);

        connection.Execute(
            """
            INSERT INTO event(name, created_at)
            SELECT 'Festa ' || substr(opened_at, 1, 4), MIN(opened_at)
            FROM session
            GROUP BY substr(opened_at, 1, 4);
            """, transaction: tx);

        // Matched on the name this migration just wrote. Safe because it runs
        // before any event a person could have named, and only once.
        connection.Execute(
            """
            UPDATE session SET event_id = (
              SELECT e.id FROM event e
              WHERE e.name = 'Festa ' || substr(session.opened_at, 1, 4)
            );
            """, transaction: tx);

        // A festival whose every session is closed is over, and is stamped with the
        // last of them. One still holding an open session stays open - which also
        // keeps the "one open event at a time" rule true, since only one session
        // can be open to begin with.
        connection.Execute(
            """
            UPDATE event SET closed_at = (
              SELECT MAX(s.closed_at) FROM session s WHERE s.event_id = event.id
            )
            WHERE NOT EXISTS (
              SELECT 1 FROM session s WHERE s.event_id = event.id AND s.closed_at IS NULL
            );
            """, transaction: tx);

        connection.Execute("UPDATE setting SET value = '2' WHERE key = 'schema_version'",
            transaction: tx);

        tx.Commit();
    }

    /// <summary>Timestamped copy of the whole database. Called on session close and
    /// from Settings. An event laptop that dies at midnight should not take the
    /// night's takings with it.</summary>
    public string Backup(DateTime now) => Backup(null, $"backup-{now:yyyyMMdd-HHmmss}");

    /// <summary>
    /// Copies the database beside itself under the given name. The connection is
    /// passed in when the caller is already inside Initialize and the file is
    /// half-open; everywhere else it opens its own.
    /// </summary>
    private string Backup(SqliteConnection? existing, string name)
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        var target = System.IO.Path.Combine(dir, $"{name}.db");

        var opened = existing is null ? Open() : null;
        var source = existing ?? opened!;

        using (opened)
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
