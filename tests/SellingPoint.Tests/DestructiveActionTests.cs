using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// The three actions that destroy something a festival cannot get back: a
/// category with every product in it, the slips of people who have already paid,
/// and the night's session. Each used to happen on a single tap of a button that
/// looked like its harmless neighbour.
///
/// These check the first tap does nothing but ask.
/// </summary>
public class DestructiveActionTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 21, 0, 0);

    [Fact]
    public void One_tap_does_not_delete_a_product()
    {
        using var t = new TempDb();
        var gestao = new GestaoViewModel(new AppServices(t.Path));
        gestao.Load();
        gestao.SelectedProduct = gestao.ProductRows.First();

        var name = gestao.SelectedProduct!.Product.Name;
        gestao.DeleteProductCommand.Execute(null);

        Assert.True(gestao.ProductDeleteArmed);
        Assert.Contains(name, gestao.StatusMessage);
        Assert.Contains(t.Catalog.GetProducts(), p => p.Name == name);

        gestao.DeleteProductCommand.Execute(null);

        Assert.False(gestao.ProductDeleteArmed);
        Assert.DoesNotContain(t.Catalog.GetProducts(), p => p.Name == name);
    }

    [Fact]
    public void Choosing_another_product_takes_the_aim_off_the_first()
    {
        // The list selects a row of its own accord after a delete, so an armed
        // button must not stay armed at whatever the selection lands on next.
        using var t = new TempDb();
        var gestao = new GestaoViewModel(new AppServices(t.Path));
        gestao.Load();
        gestao.SelectedProduct = gestao.ProductRows.First();

        gestao.DeleteProductCommand.Execute(null);
        Assert.True(gestao.ProductDeleteArmed);

        gestao.SelectedProduct = gestao.ProductRows.Last();
        Assert.False(gestao.ProductDeleteArmed);

        var count = t.Catalog.GetProducts().Count;
        gestao.DeleteProductCommand.Execute(null);
        Assert.Equal(count, t.Catalog.GetProducts().Count);
    }

    [Fact]
    public void The_question_about_a_category_says_how_many_products_go_with_it()
    {
        // Deleting a category takes its products by database cascade. The count
        // used to appear only afterwards, in a status line, once they were gone.
        using var t = new TempDb();
        var gestao = new GestaoViewModel(new AppServices(t.Path));
        gestao.Load();
        gestao.SelectedCategory = gestao.CategoryRows.First(c => c.Category.Name == "Bebidas");

        gestao.DeleteCategoryCommand.Execute(null);

        Assert.True(gestao.CategoryDeleteArmed);
        Assert.Contains("6", gestao.StatusMessage);          // the six drinks seeded
        Assert.Contains(t.Catalog.GetCategories(), c => c.Name == "Bebidas");

        gestao.DeleteCategoryCommand.Execute(null);
        Assert.DoesNotContain(t.Catalog.GetCategories(), c => c.Name == "Bebidas");
    }

    [Fact]
    public void One_tap_does_not_close_the_night()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Sales.OpenSession("Festa", 5000, Evening);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.CountedEntry = "230,50";

        relatorios.CloseSessionCommand.Execute(null);

        Assert.True(relatorios.CloseArmed);
        Assert.Contains("230,50", relatorios.StatusMessage);
        Assert.True(t.Sales.GetSessions().Single().IsOpen);

        relatorios.CloseSessionCommand.Execute(null);
        Assert.False(t.Sales.GetSessions().Single().IsOpen);
    }

    [Fact]
    public void Changing_the_counted_amount_takes_the_aim_off_the_old_one()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Sales.OpenSession("Festa", 0, Evening);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.CountedEntry = "100,00";
        relatorios.CloseSessionCommand.Execute(null);
        Assert.True(relatorios.CloseArmed);

        relatorios.CountedEntry = "200,00";

        Assert.False(relatorios.CloseArmed);
        Assert.True(t.Sales.GetSessions().Single().IsOpen);
    }

    [Fact]
    public void One_tap_does_not_throw_away_the_queue()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);

        // Paused, or the file transport prints it before the test can look.
        services.Print.Pause();
        services.Print.EnqueueText("TESTE", ["um talão pago"]);

        var diagnostics = new PrinterDiagnosticsViewModel(services);
        diagnostics.Refresh();

        diagnostics.DiscardQueueCommand.Execute(null);

        Assert.True(diagnostics.DiscardArmed);
        Assert.Equal(1, services.Print.PendingCount);

        diagnostics.DiscardQueueCommand.Execute(null);

        Assert.False(diagnostics.DiscardArmed);
        Assert.Equal(0, services.Print.PendingCount);
    }

    [Fact]
    public void Opening_the_printer_panel_starts_with_nothing_armed()
    {
        // Arming is not cleared on refresh, because the print worker refreshes
        // this panel every three seconds and would cancel the question before
        // anyone could answer it. It is cleared on the way in instead.
        using var t = new TempDb();
        var services = new AppServices(t.Path);

        services.Print.Pause();
        services.Print.EnqueueText("TESTE", ["um talão pago"]);

        var diagnostics = new PrinterDiagnosticsViewModel(services);
        diagnostics.DiscardQueueCommand.Execute(null);
        Assert.True(diagnostics.DiscardArmed);

        diagnostics.Refresh();
        Assert.True(diagnostics.DiscardArmed);       // survives the worker's tick

        diagnostics.Disarm();
        Assert.False(diagnostics.DiscardArmed);
    }
}
