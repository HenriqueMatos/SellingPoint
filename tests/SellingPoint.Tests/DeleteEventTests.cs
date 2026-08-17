using Dapper;
using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// Taking a festival off the machine. The till is shared, and a committee should
/// not have to leave last year's takings on a computer other people use.
///
/// What must survive is the setup - products, prices, categories, settings - so the
/// next festival does not start by retyping forty products. What must go is
/// everything about the festival itself, with nothing left behind pointing at rows
/// that no longer exist.
/// </summary>
public class DeleteEventTests
{
    private static readonly DateTime Friday = new(2026, 8, 14, 21, 0, 0);

    private static Event Festival(TempDb t, out int nightId, bool close = true)
    {
        var festival = t.Sales.OpenEvent("Festa 2026", Friday);
        var night = t.Sales.OpenSession("Sexta", 5000, Friday, festival.Id);
        nightId = night.Id;

        var products = t.Catalog.GetProducts();
        var beer = products.First(p => p.Name == "Cerveja");
        beer.TrackStock = true;
        beer.StockQty = 100;
        t.Catalog.UpdateProduct(beer);

        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Cerveja"), 10);
        cart.Add(products.First(p => p.Name == "Bifana"), 4);

        var sale = t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 100_000, Friday.AddHours(1)),
            night.Id);

        t.PrintQueue.Enqueue([new PrintJob
        {
            SaleId = sale.Id, Title = "#0001", Payload = [1, 2, 3],
            Preview = "x", CreatedAt = Friday.AddHours(1)
        }]);

        t.Sales.RecordCashMovement(night.Id, -2000, "Levado ao carro", Friday.AddHours(2));
        t.Catalog.AdjustStock(beer.Id, 24, "Reposição", Friday.AddHours(3), night.Id);

        if (close) t.Sales.CloseSession(night.Id, 1, Friday.AddHours(5));

        return festival;
    }

    private static int Count(TempDb t, string table)
    {
        using var c = t.Db.Open();
        return c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
    }

    [Fact]
    public void The_festival_and_everything_under_it_goes()
    {
        using var t = new TempDb();
        var festival = Festival(t, out _);

        Assert.Equal(1, Count(t, "sale"));
        Assert.Equal(2, Count(t, "sale_line"));

        t.Sales.DeleteEvent(festival.Id);

        Assert.Equal(0, Count(t, "event"));
        Assert.Equal(0, Count(t, "session"));
        Assert.Equal(0, Count(t, "sale"));
        Assert.Equal(0, Count(t, "sale_line"));
        Assert.Equal(0, Count(t, "cash_movement"));
        Assert.Equal(0, Count(t, "stock_adjustment"));
        Assert.Equal(0, Count(t, "print_job"));
    }

    [Fact]
    public void The_till_is_still_set_up_afterwards()
    {
        // The whole point of deleting the festival rather than the database: the
        // next one must not start by retyping forty products and their prices.
        using var t = new TempDb();
        var festival = Festival(t, out _);
        t.Settings.Set(SettingKeys.TicketHeader, "FESTA DO CALVÁRIO");

        t.Sales.DeleteEvent(festival.Id);

        Assert.Equal(3, t.Catalog.GetCategories().Count);
        Assert.Equal(14, t.Catalog.GetProducts().Count);
        Assert.Equal(150, t.Catalog.GetProducts().First(p => p.Name == "Cerveja").PriceCents);
        Assert.Equal("FESTA DO CALVÁRIO", t.Settings.GetString(SettingKeys.TicketHeader, ""));
    }

    [Fact]
    public void Another_festival_on_the_same_machine_is_left_alone()
    {
        using var t = new TempDb();
        var last = Festival(t, out _);

        var thisYear = t.Sales.OpenEvent("Festa 2027", Friday.AddYears(1));
        var night = t.Sales.OpenSession("Sexta", 0, Friday.AddYears(1), thisYear.Id);
        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Cerveja"), 3);
        t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 10_000, Friday.AddYears(1).AddHours(1)),
            night.Id);
        t.Sales.CloseSession(night.Id, 450, Friday.AddYears(1).AddHours(5));

        t.Sales.DeleteEvent(last.Id);

        Assert.Equal("Festa 2027", t.Sales.GetEvents().Single().Name);
        Assert.Single(t.Sales.GetSessions());
        Assert.Equal(1, Count(t, "sale"));
        Assert.Equal(450, new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single()).CashCents);
    }

    [Fact]
    public void A_festival_with_a_night_still_selling_is_refused()
    {
        // It would be deleting a till somebody is standing at, and that night's
        // takings have not been counted.
        using var t = new TempDb();
        var festival = Festival(t, out _, close: false);

        var refused = Assert.Throws<InvalidOperationException>(() => t.Sales.DeleteEvent(festival.Id));

        Assert.Contains("Sexta", refused.Message);
        Assert.Single(t.Sales.GetEvents());
        Assert.Equal(1, Count(t, "sale"));
    }

    [Fact]
    public void Stock_counts_are_not_wound_back()
    {
        // Deleting the paperwork must not put beer back on the shelf. What is in
        // the storeroom now is a fact about the storeroom, not about the festival.
        using var t = new TempDb();
        var festival = Festival(t, out _);

        var before = t.Catalog.GetProducts().First(p => p.Name == "Cerveja").StockQty;
        t.Sales.DeleteEvent(festival.Id);

        Assert.Equal(before, t.Catalog.GetProducts().First(p => p.Name == "Cerveja").StockQty);
    }

    // --- the screen ---------------------------------------------------------

    [Fact]
    public void The_delete_button_does_nothing_until_the_festival_has_been_exported()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        var festival = Festival(t, out _);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.SelectedSession = relatorios.Sessions.First(s => s.Event is not null);

        Assert.False(relatorios.CanDeleteEvent);

        relatorios.DeleteEventCommand.Execute(null);
        relatorios.DeleteEventCommand.Execute(null);

        Assert.Single(t.Sales.GetEvents());
        Assert.Contains("Exporte", relatorios.StatusMessage);
    }

    [Fact]
    public void Exporting_first_writes_the_file_and_the_backup_and_says_where()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        Festival(t, out _);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.SelectedSession = relatorios.Sessions.First(s => s.Event is not null);

        relatorios.ExportForDeleteCommand.Execute(null);

        Assert.True(relatorios.CanDeleteEvent);
        Assert.Contains("relatórios", relatorios.StatusMessage);
        Assert.Contains("backup-", relatorios.StatusMessage);
    }

    [Fact]
    public void Two_taps_after_exporting_takes_the_festival_off_the_machine()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        Festival(t, out _);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.SelectedSession = relatorios.Sessions.First(s => s.Event is not null);
        relatorios.ExportForDeleteCommand.Execute(null);

        relatorios.DeleteEventCommand.Execute(null);
        Assert.True(relatorios.DeleteArmed);
        Assert.Single(t.Sales.GetEvents());

        relatorios.DeleteEventCommand.Execute(null);
        Assert.Empty(t.Sales.GetEvents());
        Assert.Equal(0, Count(t, "sale"));
    }

    [Fact]
    public void Choosing_a_different_festival_needs_its_own_export()
    {
        // Otherwise an export of last year's festival would unlock the delete on
        // this year's, which is the one nobody has a copy of.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Print.Pause();
        Festival(t, out _);

        var other = t.Sales.OpenEvent("Festa 2027", Friday.AddYears(1));
        var night = t.Sales.OpenSession("Sexta", 0, Friday.AddYears(1), other.Id);
        t.Sales.CloseSession(night.Id, 0, Friday.AddYears(1).AddHours(5));

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();

        var rows = relatorios.Sessions.Where(s => s.Event is not null).ToList();
        relatorios.SelectedSession = rows[0];
        relatorios.ExportForDeleteCommand.Execute(null);
        Assert.True(relatorios.CanDeleteEvent);

        relatorios.SelectedSession = rows[1];

        Assert.False(relatorios.CanDeleteEvent);
        Assert.False(relatorios.DeleteArmed);
    }
}
