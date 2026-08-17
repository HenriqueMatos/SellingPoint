using Dapper;
using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// Closing the last night of a festival cannot close the festival - nobody knows
/// it was the last one. So a festival stays open until somebody says otherwise,
/// and next August's first night would join last August's unless the screen makes
/// that visible and offers a way out.
///
/// Found by attacking the design rather than by anything failing.
/// </summary>
public class StaleFestivalTests
{
    private static readonly DateTime LastAugust = new(2025, 8, 15, 21, 0, 0);
    private static readonly DateTime ThisAugust = new(2026, 8, 14, 21, 0, 0);

    [Fact]
    public void The_panel_names_the_festival_a_night_would_join()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var old = services.Sales.OpenEvent("Festa 2025", LastAugust);
        var night = services.Sales.OpenSession("Sábado", 0, LastAugust, old.Id);
        services.Sales.CloseSession(night.Id, 0, LastAugust.AddHours(5));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenSessionPanelCommand.Execute(null);

        Assert.False(venda.NeedsEvent);
        Assert.Contains("Festa 2025", venda.EventText);
        Assert.Contains("15/08/2025", venda.EventText);
        Assert.False(venda.AsksForEventName);
    }

    [Fact]
    public void Ticking_start_a_new_one_asks_for_its_name()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        services.Sales.OpenEvent("Festa 2025", LastAugust);

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenSessionPanelCommand.Execute(null);

        Assert.False(venda.AsksForEventName);

        venda.StartNewEvent = true;

        Assert.True(venda.AsksForEventName);
        Assert.Equal($"Festa {DateTime.Now:yyyy}", venda.EventNameEntry);
    }

    [Fact]
    public void A_new_festival_ends_the_one_before_it_and_takes_the_night()
    {
        // The failure this exists for: without it, every 2026 sale would be
        // reported under Festa 2025 and Festa 2026 would never exist.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var old = services.Sales.OpenEvent("Festa 2025", LastAugust);
        var lastYear = services.Sales.OpenSession("Sábado", 0, LastAugust, old.Id);
        services.Sales.CloseSession(lastYear.Id, 0, LastAugust.AddHours(5));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenSessionPanelCommand.Execute(null);
        venda.StartNewEvent = true;
        venda.EventNameEntry = "Festa 2026";
        venda.SessionNameEntry = "Sexta";
        venda.ConfirmOpenSessionCommand.Execute(null);

        var events = services.Sales.GetEvents();
        Assert.Equal(2, events.Count);

        var started = Assert.Single(events, e => e.Name == "Festa 2026");
        Assert.True(started.IsOpen);
        Assert.False(Assert.Single(events, e => e.Name == "Festa 2025").IsOpen);

        // Tonight is under the new one.
        Assert.Equal(started.Id, services.Sales.GetOpenSession()!.EventId);
    }

    [Fact]
    public void Leaving_it_unticked_joins_the_festival_already_running()
    {
        // The everyday case: the second and third nights of one festival must not
        // ask anybody anything.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();

        var festival = services.Sales.OpenEvent("Festa do Calvário", ThisAugust);
        var friday = services.Sales.OpenSession("Sexta", 0, ThisAugust, festival.Id);
        services.Sales.CloseSession(friday.Id, 0, ThisAugust.AddHours(5));

        var venda = new VendaViewModel(services);
        venda.Load();
        venda.OpenSessionPanelCommand.Execute(null);
        venda.SessionNameEntry = "Sábado";
        venda.ConfirmOpenSessionCommand.Execute(null);

        Assert.Single(services.Sales.GetEvents());
        Assert.Equal(festival.Id, services.Sales.GetOpenSession()!.EventId);
        Assert.Equal(2, services.Sales.GetSessions(festival.Id).Count);
    }

    [Fact]
    public void The_migration_copies_the_file_before_it_changes_it()
    {
        // The only place the program rewrites the shape of a live database, once
        // and unattended. Everything destructive the screens do copies first.
        var path = Path.Combine(Path.GetTempPath(), $"sp-premig-{Guid.NewGuid():N}.db");

        try
        {
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                c.Execute(
                    """
                    CREATE TABLE setting (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    CREATE TABLE session (
                      id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL,
                      opened_at TEXT NOT NULL, closed_at TEXT,
                      opening_float_cents INTEGER NOT NULL DEFAULT 0, closing_counted_cents INTEGER);
                    INSERT INTO setting(key, value) VALUES ('schema_version', '1');
                    INSERT INTO session(name, opened_at, closed_at) VALUES ('Sábado', '2026-08-15 21:00:00', '2026-08-16 02:00:00');
                    """);
            }

            new Db(path).Initialize(seedIfEmpty: false);

            var copy = Path.Combine(Path.GetDirectoryName(path)!, "antes-da-migracao.db");
            Assert.True(File.Exists(copy), "a migração devia deixar uma cópia do ficheiro anterior");

            // And the copy is the database as it was: no event_id on session.
            using var before = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={copy}");
            before.Open();
            var columns = before.Query<string>("SELECT name FROM pragma_table_info('session')").ToList();

            Assert.DoesNotContain("event_id", columns);
            Assert.Equal(1, before.ExecuteScalar<int>("SELECT COUNT(*) FROM session"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!,
                         Path.GetFileNameWithoutExtension(path) + "*"))
            {
                try { File.Delete(f); } catch (IOException) { }
            }

            var copy = Path.Combine(Path.GetDirectoryName(path)!, "antes-da-migracao.db");
            try { File.Delete(copy); } catch (IOException) { }
        }
    }
}
