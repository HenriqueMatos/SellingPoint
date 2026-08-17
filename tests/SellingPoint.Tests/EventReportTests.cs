namespace SellingPoint.Tests;

/// <summary>
/// A festival of several nights, added up. Each night keeps its own float and its
/// own count, because whoever stands at the till changes; the festival is the
/// number the committee actually wants the morning after.
/// </summary>
public class EventReportTests
{
    private static readonly DateTime Friday = new(2026, 8, 14, 21, 0, 0);

    private static void Sell(TempDb t, int sessionId, string product, int qty, DateTime at,
        PaymentMethod method = PaymentMethod.Cash)
    {
        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == product), qty);

        t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                method, 1_000_000, at),
            sessionId);
    }

    /// <summary>Two nights of one festival, both counted and both correct.</summary>
    private static (Event Festival, EventReport Report) TwoNights(TempDb t)
    {
        var festival = t.Sales.OpenEvent("Festa do Calvário 2026", Friday);

        var friday = t.Sales.OpenSession("Sexta", 5000, Friday, festival.Id);
        Sell(t, friday.Id, "Cerveja", 20, Friday.AddHours(1));                       // 30,00
        Sell(t, friday.Id, "Bifana", 10, Friday.AddHours(2), PaymentMethod.Card);    // 30,00
        t.Sales.CloseSession(friday.Id, 5000 + 3000, Friday.AddHours(5));

        var saturday = t.Sales.OpenSession("Sábado", 5000, Friday.AddDays(1), festival.Id);
        Sell(t, saturday.Id, "Cerveja", 30, Friday.AddDays(1).AddHours(1));          // 45,00
        Sell(t, saturday.Id, "Bolo", 10, Friday.AddDays(1).AddHours(2));             // 15,00
        t.Sales.CloseSession(saturday.Id, 5000 + 6000, Friday.AddDays(1).AddHours(5));

        return (festival, new ReportRepository(t.Db)
            .BuildForEvent(festival, t.Sales.GetSessions(festival.Id)));
    }

    [Fact]
    public void The_festival_is_its_nights_added_up()
    {
        using var t = new TempDb();
        var (_, report) = TwoNights(t);

        Assert.Equal(2, report.Nights.Count);
        Assert.Equal(4, report.SalesCount);
        Assert.Equal(3000 + 4500 + 1500, report.CashCents);
        Assert.Equal(3000, report.CardCents);
        Assert.Equal(12_000, report.TotalCents);
        Assert.Equal(10_000, report.FloatCents);
    }

    [Fact]
    public void The_same_product_on_two_nights_is_one_line()
    {
        using var t = new TempDb();
        var (_, report) = TwoNights(t);

        var beer = Assert.Single(report.Products, p => p.Name == "Cerveja");

        Assert.Equal(50, beer.Units);
        Assert.Equal(7500, beer.TotalCents);

        // And it is the biggest line, so the list is worth reading top down.
        Assert.Equal("Cerveja", report.Products[0].Name);
    }

    [Fact]
    public void Categories_add_up_across_the_nights_too()
    {
        using var t = new TempDb();
        var (_, report) = TwoNights(t);

        Assert.Equal(7500, Assert.Single(report.Categories, c => c.Name == "Bebidas").TotalCents);
        Assert.Equal(3000, Assert.Single(report.Categories, c => c.Name == "Comida").TotalCents);
    }

    [Fact]
    public void Two_correct_nights_make_a_festival_with_no_difference()
    {
        using var t = new TempDb();
        var (_, report) = TwoNights(t);

        Assert.Equal(19_000, report.ExpectedCashCents);      // 100,00 de fundos + 90,00 em dinheiro
        Assert.Equal(19_000, report.CountedCashCents);
        Assert.Equal(0, report.VarianceCents);
        Assert.Equal(0, report.UncountedNights);
    }

    [Fact]
    public void A_night_short_shows_as_the_festival_being_short_by_that_much()
    {
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa", Friday);

        var friday = t.Sales.OpenSession("Sexta", 0, Friday, festival.Id);
        Sell(t, friday.Id, "Cerveja", 10, Friday.AddHours(1));                  // 15,00
        t.Sales.CloseSession(friday.Id, 1000, Friday.AddHours(5));              // faltam 5,00

        var saturday = t.Sales.OpenSession("Sábado", 0, Friday.AddDays(1), festival.Id);
        Sell(t, saturday.Id, "Cerveja", 10, Friday.AddDays(1).AddHours(1));
        t.Sales.CloseSession(saturday.Id, 1500, Friday.AddDays(1).AddHours(5)); // certo

        var report = new ReportRepository(t.Db)
            .BuildForEvent(festival, t.Sales.GetSessions(festival.Id));

        Assert.Equal(-500, report.VarianceCents);
    }

    [Fact]
    public void A_night_nobody_counted_is_left_out_of_the_difference_and_said_out_loud()
    {
        // Counting an uncounted night as zero would report the festival as short by
        // that whole night's takings, and send somebody looking for money that is
        // sitting in a box nobody has opened.
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa", Friday);

        var friday = t.Sales.OpenSession("Sexta", 0, Friday, festival.Id);
        Sell(t, friday.Id, "Cerveja", 10, Friday.AddHours(1));
        t.Sales.CloseSession(friday.Id, 1500, Friday.AddHours(5));             // contada e certa

        var saturday = t.Sales.OpenSession("Sábado", 0, Friday.AddDays(1), festival.Id);
        Sell(t, saturday.Id, "Cerveja", 100, Friday.AddDays(1).AddHours(1));   // 150,00 por contar

        var report = new ReportRepository(t.Db)
            .BuildForEvent(festival, t.Sales.GetSessions(festival.Id));

        Assert.Equal(1, report.UncountedNights);
        Assert.Equal(0, report.VarianceCents);
        Assert.Equal(16_500, report.TotalCents);   // as vendas contam na mesma
    }

    [Fact]
    public void A_festival_where_nothing_was_counted_has_no_difference_to_show()
    {
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa", Friday);
        var friday = t.Sales.OpenSession("Sexta", 0, Friday, festival.Id);
        Sell(t, friday.Id, "Cerveja", 10, Friday.AddHours(1));

        var report = new ReportRepository(t.Db)
            .BuildForEvent(festival, t.Sales.GetSessions(festival.Id));

        Assert.Null(report.VarianceCents);
    }

    [Fact]
    public void Money_carried_out_on_any_night_counts_against_the_festival()
    {
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa", Friday);
        var friday = t.Sales.OpenSession("Sexta", 5000, Friday, festival.Id);
        Sell(t, friday.Id, "Cerveja", 100, Friday.AddHours(1));                 // 150,00
        t.Sales.RecordCashMovement(friday.Id, -10_000, "Levado ao carro", Friday.AddHours(2));

        var report = new ReportRepository(t.Db)
            .BuildForEvent(festival, t.Sales.GetSessions(festival.Id));

        Assert.Equal(-10_000, report.CashMovementCents);
        Assert.Equal(5000 + 15_000 - 10_000, report.ExpectedCashCents);
        Assert.Equal("Levado ao carro", Assert.Single(report.CashMovements).Reason);
    }

    [Fact]
    public void The_export_carries_the_festival_and_a_line_for_each_night()
    {
        using var t = new TempDb();
        var (_, report) = TwoNights(t);

        var csv = ReportRepository.ToCsv(report);

        Assert.Contains("Festa;Festa do Calvário 2026", csv);
        Assert.Contains("Noites;2", csv);
        Assert.Contains("Total;120,00", csv);
        Assert.Contains("Sexta;", csv);
        Assert.Contains("Sábado;", csv);
        Assert.Contains("Cerveja;Bebidas;50;75,00", csv);
    }

    [Fact]
    public void A_festival_cannot_be_closed_while_a_night_is_still_selling()
    {
        // Its takings are not counted until the night closes, so the festival total
        // would be short by them and signed off anyway.
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa", Friday);
        t.Sales.OpenSession("Sexta", 0, Friday, festival.Id);

        var refused = Assert.Throws<InvalidOperationException>(
            () => t.Sales.CloseEvent(festival.Id, Friday.AddHours(5)));

        Assert.Contains("Sexta", refused.Message);
        Assert.True(t.Sales.GetEvents().Single().IsOpen);
    }

    [Fact]
    public void A_night_opened_without_naming_a_festival_joins_the_open_one()
    {
        // The invariant the repository keeps: no session lives outside a festival.
        using var t = new TempDb();
        var festival = t.Sales.OpenEvent("Festa do Calvário 2026", Friday);

        var night = t.Sales.OpenSession("Sexta", 0, Friday);

        Assert.Equal(festival.Id, night.EventId);
    }

    [Fact]
    public void With_no_festival_open_the_first_night_starts_one()
    {
        using var t = new TempDb();

        var night = t.Sales.OpenSession("Sexta", 0, Friday);

        var festival = Assert.Single(t.Sales.GetEvents());
        Assert.Equal(festival.Id, night.EventId);
        Assert.True(festival.IsOpen);
    }
}
