using Dapper;
using Microsoft.Data.Sqlite;

namespace SellingPoint.Tests;

/// <summary>
/// The migration that puts every session inside a festival, run against a database
/// built the way version 1 built them - with real sessions and real sales in it.
///
/// This is the only code in the project that touches a shape it did not create, on
/// a file somebody's takings live in. It is tested against a hand-built version 1
/// database rather than against the current schema, because testing it against the
/// schema it is meant to upgrade from is the whole point.
/// </summary>
public class MigrationTests
{
    /// <summary>A database exactly as version 1 left it: no event table, no event_id.</summary>
    private sealed class VersionOne : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sp-v1-{Guid.NewGuid():N}.db");

        public VersionOne()
        {
            using var c = Open();
            c.Execute(
                """
                CREATE TABLE setting (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE category (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL,
                  color TEXT NOT NULL DEFAULT '#3A7BD5', sort_order INTEGER NOT NULL DEFAULT 0,
                  print_group TEXT NOT NULL DEFAULT 'Bar', slip_mode TEXT NOT NULL DEFAULT 'Grouped');
                CREATE TABLE product (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  category_id INTEGER NOT NULL REFERENCES category(id) ON DELETE CASCADE,
                  name TEXT NOT NULL, price_cents INTEGER NOT NULL,
                  sort_order INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1,
                  track_stock INTEGER NOT NULL DEFAULT 0, stock_qty INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE session (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL,
                  opened_at TEXT NOT NULL, closed_at TEXT,
                  opening_float_cents INTEGER NOT NULL DEFAULT 0, closing_counted_cents INTEGER);
                CREATE TABLE sale (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  session_id INTEGER NOT NULL REFERENCES session(id),
                  ticket_number INTEGER NOT NULL, created_at TEXT NOT NULL,
                  total_cents INTEGER NOT NULL, payment_method TEXT NOT NULL,
                  cash_received_cents INTEGER NOT NULL DEFAULT 0, change_cents INTEGER NOT NULL DEFAULT 0);
                CREATE UNIQUE INDEX ux_sale_ticket ON sale(session_id, ticket_number);
                CREATE TABLE sale_line (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  sale_id INTEGER NOT NULL REFERENCES sale(id) ON DELETE CASCADE,
                  product_id INTEGER NOT NULL, product_name TEXT NOT NULL,
                  unit_price_cents INTEGER NOT NULL, category_name TEXT NOT NULL,
                  print_group TEXT NOT NULL, slip_mode TEXT NOT NULL,
                  qty INTEGER NOT NULL, line_total_cents INTEGER NOT NULL);
                CREATE TABLE stock_adjustment (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  product_id INTEGER NOT NULL REFERENCES product(id) ON DELETE CASCADE,
                  delta INTEGER NOT NULL, reason TEXT NOT NULL DEFAULT '',
                  created_at TEXT NOT NULL, session_id INTEGER);
                CREATE TABLE print_job (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, sale_id INTEGER, title TEXT NOT NULL,
                  payload BLOB NOT NULL, preview TEXT NOT NULL DEFAULT '',
                  created_at TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
                  last_error TEXT, printed_at TEXT);
                INSERT INTO setting(key, value) VALUES ('schema_version', '1');
                """);
        }

        public SqliteConnection Open()
        {
            var c = new SqliteConnection($"Data Source={Path}");
            c.Open();
            return c;
        }

        /// <summary>A night, with a sale on it, written the version 1 way.</summary>
        public int AddNight(string name, string openedAt, string? closedAt, int floatCents, int totalCents)
        {
            using var c = Open();
            var sessionId = c.ExecuteScalar<int>(
                """
                INSERT INTO session(name, opened_at, closed_at, opening_float_cents)
                VALUES(@name, @openedAt, @closedAt, @floatCents);
                SELECT last_insert_rowid();
                """, new { name, openedAt, closedAt, floatCents });

            c.Execute(
                """
                INSERT INTO sale(session_id, ticket_number, created_at, total_cents, payment_method)
                VALUES(@sessionId, 1, @openedAt, @totalCents, 'Cash');
                """, new { sessionId, openedAt, totalCents });

            return sessionId;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!,
                         System.IO.Path.GetFileNameWithoutExtension(Path) + "*"))
            {
                try { File.Delete(f); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public void An_old_database_keeps_every_session_and_every_sale()
    {
        // The thing that must not happen: an upgrade that loses a night's takings.
        using var v1 = new VersionOne();
        v1.AddNight("Sexta", "2026-08-14 21:00:00", "2026-08-15 02:00:00", 5000, 12_345);
        v1.AddNight("Sábado", "2026-08-15 21:00:00", "2026-08-16 02:00:00", 5000, 23_456);

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var sales = new SalesRepository(new Db(v1.Path));
        var sessions = sales.GetSessions();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.Name == "Sexta");
        Assert.Contains(sessions, s => s.Name == "Sábado");

        using var c = v1.Open();
        Assert.Equal(2, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sale"));
        Assert.Equal(12_345 + 23_456, c.ExecuteScalar<int>("SELECT SUM(total_cents) FROM sale"));
    }

    [Fact]
    public void The_nights_of_one_year_become_one_festival()
    {
        using var v1 = new VersionOne();
        v1.AddNight("Sexta", "2026-08-14 21:00:00", "2026-08-15 02:00:00", 0, 100);
        v1.AddNight("Sábado", "2026-08-15 21:00:00", "2026-08-16 02:00:00", 0, 200);
        v1.AddNight("Domingo", "2026-08-16 21:00:00", "2026-08-17 02:00:00", 0, 300);

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var events = new SalesRepository(new Db(v1.Path)).GetEvents();
        var festival = Assert.Single(events);

        Assert.Equal("Festa 2026", festival.Name);
        Assert.Equal(3, new SalesRepository(new Db(v1.Path)).GetSessions(festival.Id).Count);
    }

    [Fact]
    public void Two_years_become_two_festivals()
    {
        using var v1 = new VersionOne();
        v1.AddNight("Sábado", "2025-08-16 21:00:00", "2025-08-17 02:00:00", 0, 100);
        v1.AddNight("Sábado", "2026-08-15 21:00:00", "2026-08-16 02:00:00", 0, 200);

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var sales = new SalesRepository(new Db(v1.Path));
        var names = sales.GetEvents().Select(e => e.Name).Order().ToList();

        Assert.Equal(["Festa 2025", "Festa 2026"], names);
        Assert.All(sales.GetSessions(), s => Assert.NotNull(s.EventId));
    }

    [Fact]
    public void A_festival_whose_nights_are_all_closed_is_closed_too()
    {
        using var v1 = new VersionOne();
        v1.AddNight("Sexta", "2026-08-14 21:00:00", "2026-08-15 02:00:00", 0, 100);
        v1.AddNight("Sábado", "2026-08-15 21:00:00", "2026-08-16 03:30:00", 0, 200);

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var festival = Assert.Single(new SalesRepository(new Db(v1.Path)).GetEvents());

        Assert.False(festival.IsOpen);
        Assert.Equal(new DateTime(2026, 8, 16, 3, 30, 0), festival.ClosedAt);
    }

    [Fact]
    public void A_festival_still_selling_stays_open()
    {
        // Upgrading mid-event must not close the night out from under whoever is at
        // the till, and must leave exactly one event open for the next session.
        using var v1 = new VersionOne();
        v1.AddNight("Sexta", "2026-08-14 21:00:00", "2026-08-15 02:00:00", 0, 100);
        v1.AddNight("Sábado", "2026-08-15 21:00:00", closedAt: null, 5000, 200);

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var sales = new SalesRepository(new Db(v1.Path));
        var festival = Assert.Single(sales.GetEvents());

        Assert.True(festival.IsOpen);
        Assert.Equal("Sábado", sales.GetOpenSession()!.Name);
        Assert.Single(sales.GetEvents(), e => e.IsOpen);
    }

    [Fact]
    public void A_database_with_no_sessions_yet_gains_no_festivals()
    {
        using var v1 = new VersionOne();

        new Db(v1.Path).Initialize(seedIfEmpty: false);

        Assert.Empty(new SalesRepository(new Db(v1.Path)).GetEvents());
    }

    [Fact]
    public void Running_it_twice_changes_nothing_the_second_time()
    {
        // Initialize runs on every startup. A migration that is not idempotent
        // would double the festivals on the second launch, or fail on the ALTER.
        using var v1 = new VersionOne();
        v1.AddNight("Sábado", "2026-08-15 21:00:00", "2026-08-16 02:00:00", 0, 200);

        new Db(v1.Path).Initialize(seedIfEmpty: false);
        new Db(v1.Path).Initialize(seedIfEmpty: false);
        new Db(v1.Path).Initialize(seedIfEmpty: false);

        var sales = new SalesRepository(new Db(v1.Path));

        Assert.Single(sales.GetEvents());
        Assert.Single(sales.GetSessions());

        using var c = v1.Open();
        Assert.Equal("2", c.ExecuteScalar<string>("SELECT value FROM setting WHERE key = 'schema_version'"));
    }

    [Fact]
    public void A_database_created_today_is_already_at_the_current_version()
    {
        // The fresh path must not run the migration at all: schema.sql builds the
        // column itself, so the ALTER would fail.
        using var t = new TempDb();
        using var c = t.Db.Open();

        Assert.Equal("2", c.ExecuteScalar<string>("SELECT value FROM setting WHERE key = 'schema_version'"));
    }
}
