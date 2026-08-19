using SellingPoint.App;
using SellingPoint.App.ViewModels;

namespace SellingPoint.Tests;

/// <summary>
/// Cash carried out of the drawer mid-evening. Somebody walks most of the night's
/// takings to a car at eleven, because a cash box in a field is not where it
/// should spend the night.
///
/// Until this existed, that walk destroyed the one number that catches an error or
/// a theft - the count against what was expected - and the only way round it was
/// to close the session and start another, splitting the night's report in two.
/// </summary>
public class CashMovementTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 21, 0, 0);

    private static Sale Sell(TempDb t, int sessionId, string product, int qty, DateTime at)
    {
        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == product), qty);

        return t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 100_000, at),
            sessionId);
    }

    /// <summary>The movements of a night, read the way the closing report reads them.</summary>
    private static IReadOnlyList<CashMovement> Movements(TempDb t, int sessionId) =>
        new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single(s => s.Id == sessionId)).CashMovements;

    [Fact]
    public void Money_carried_out_is_not_expected_back_in_the_box()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 5000, Evening);

        Sell(t, session.Id, "Cerveja", 10, Evening.AddHours(1));      // 15,00 em dinheiro
        t.Sales.RecordCashMovement(session.Id, -10_000, "Levado ao carro", Evening.AddHours(2));

        var report = new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single());

        Assert.Equal(1500, report.CashCents);
        Assert.Equal(-10_000, report.CashMovementCents);

        // 50,00 de fundo + 15,00 vendido - 100,00 levado = -35,00 esperado na caixa.
        Assert.Equal(5000 + 1500 - 10_000, report.ExpectedCashCents);
    }

    [Fact]
    public void The_takings_are_untouched_by_the_walk_to_the_car()
    {
        // What was sold is what was sold. A sangria moves where the money is, not
        // how much came in - it must never look like a smaller night.
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Evening);

        Sell(t, session.Id, "Cerveja", 10, Evening.AddHours(1));
        t.Sales.RecordCashMovement(session.Id, -1000, "Levado ao carro", Evening.AddHours(2));

        var report = new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single());

        Assert.Equal(1500, report.CashCents);
        Assert.Equal(1500, report.TotalCents);
        Assert.Equal(1, report.SalesCount);
    }

    [Fact]
    public void With_the_walk_recorded_a_correct_count_shows_no_difference()
    {
        // The whole point. Take 100,00 out, count what is left, and the variance
        // is zero rather than a phantom 100,00 short.
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 5000, Evening);

        Sell(t, session.Id, "Cerveja", 100, Evening.AddHours(1));     // 150,00
        t.Sales.RecordCashMovement(session.Id, -10_000, "Levado ao carro", Evening.AddHours(2));

        var counted = 5000 + 15_000 - 10_000;
        t.Sales.CloseSession(session.Id, counted, Evening.AddHours(5));

        var report = new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single());

        Assert.Equal(counted, report.ExpectedCashCents);
        Assert.Equal(0, report.VarianceCents);
    }

    [Fact]
    public void Putting_change_in_counts_the_other_way()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Evening);

        t.Sales.RecordCashMovement(session.Id, 2000, "Trocos", Evening.AddHours(1));

        var report = new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single());

        Assert.Equal(2000, report.ExpectedCashCents);
        Assert.Equal(2000, report.CashMovements.Single().Cents);
    }

    [Fact]
    public void Every_movement_is_kept_with_its_reason_and_its_hour()
    {
        // A single total would balance the books and answer nothing at two in the
        // morning, which is when somebody asks where the money went.
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Evening);

        t.Sales.RecordCashMovement(session.Id, -5000, "Levado ao carro", Evening.AddHours(2));
        t.Sales.RecordCashMovement(session.Id, -3000, "Pago ao fornecedor do gelo", Evening.AddHours(3));

        var movements = Movements(t, session.Id);

        Assert.Equal(2, movements.Count);
        Assert.Equal("Levado ao carro", movements[0].Reason);
        Assert.Equal(Evening.AddHours(3), movements[1].CreatedAt);
        Assert.Equal(-8000, movements.Sum(m => m.Cents));
    }

    [Fact]
    public void A_session_with_no_movements_reports_exactly_as_it_did_before()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 5000, Evening);
        Sell(t, session.Id, "Cerveja", 2, Evening.AddHours(1));

        var report = new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single());

        Assert.Empty(report.CashMovements);
        Assert.Equal(0, report.CashMovementCents);
        Assert.Equal(5000 + 300, report.ExpectedCashCents);
        Assert.DoesNotContain("Sangrias", ReportRepository.ToCsv(report));
    }

    [Fact]
    public void The_screen_records_a_withdrawal_as_money_leaving()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Sales.OpenSession("Festa", 5000, Evening);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.WithdrawalEntry = "200,00";
        relatorios.WithdrawalReason = "Levado ao carro";
        relatorios.RecordWithdrawalCommand.Execute(null);

        var movement = Assert.Single(Movements(t, t.Sales.GetSessions().Single().Id));

        // Typed as a positive number, stored as money going out.
        Assert.Equal(-20_000, movement.Cents);
        Assert.Equal("Levado ao carro", movement.Reason);
        Assert.Equal("", relatorios.WithdrawalEntry);
    }

    [Fact]
    public void A_withdrawal_that_is_not_a_number_says_so_and_records_nothing()
    {
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        services.Sales.OpenSession("Festa", 0, Evening);

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();

        foreach (var typed in new[] { "", "abc", "0", "-50" })
        {
            relatorios.WithdrawalEntry = typed;
            relatorios.RecordWithdrawalCommand.Execute(null);

            Assert.Contains("quanto saiu", relatorios.StatusMessage);
            Assert.Empty(Movements(t, t.Sales.GetSessions().Single().Id));
        }
    }

    [Fact]
    public void A_closed_session_takes_no_more_money_out()
    {
        // It has been counted and signed off. Moving its cash afterwards would
        // change a figure somebody already agreed to.
        using var t = new TempDb();
        var services = new AppServices(t.Path);
        var session = services.Sales.OpenSession("Festa", 0, Evening);
        services.Sales.CloseSession(session.Id, 0, Evening.AddHours(5));

        var relatorios = new RelatoriosViewModel(services);
        relatorios.Load();
        relatorios.WithdrawalEntry = "50,00";
        relatorios.RecordWithdrawalCommand.Execute(null);

        Assert.Contains("sessão aberta", relatorios.StatusMessage);
        Assert.Empty(Movements(t, session.Id));
    }

    [Fact]
    public void The_export_carries_each_movement_and_their_total()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Evening);
        t.Sales.RecordCashMovement(session.Id, -12_050, "Levado ao carro", Evening.AddHours(2));

        var csv = ReportRepository.ToCsv(
            new ReportRepository(t.Db).Build(t.Sales.GetSessions().Single()));

        Assert.Contains("Sangrias e reforços;-120,50", csv);
        Assert.Contains("Levado ao carro;-120,50", csv);
    }
}
