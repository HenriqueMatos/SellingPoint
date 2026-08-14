using SellingPoint.Printing;

namespace SellingPoint.Tests;

/// <summary>
/// One night at the till, start to finish, through the real database and the real
/// printing chain. This is the rehearsal the plan calls for, run on a Mac with no
/// printer attached: the file transport stands in for the paper.
/// </summary>
public class EndToEndTests
{
    private static readonly DateTime Evening = new(2026, 8, 14, 21, 0, 0);

    [Fact]
    public void A_full_night_at_the_till()
    {
        using var t = new TempDb();
        var folder = Path.Combine(Path.GetTempPath(), $"sellingpoint-e2e-{Guid.NewGuid():N}");

        try
        {
            // --- setup: the organizer configures the night --------------------
            var categories = t.Catalog.GetCategories();
            var bebidas = categories.Single(c => c.Name == "Bebidas");
            bebidas.SlipMode = SlipMode.PerUnit;          // the bar collects a senha per drink
            t.Catalog.UpdateCategory(bebidas);

            var beer = t.Catalog.GetProducts().First(p => p.Name == "Cerveja");
            beer.TrackStock = true;
            beer.StockQty = 20;
            t.Catalog.UpdateProduct(beer);

            var printer = new TicketPrinter(new FileTransport(folder), new TicketOptions
            {
                Columns = 48, Header = "FESTA DA ALDEIA 2026", Footer = "Obrigado!"
            });

            // --- open the till ------------------------------------------------
            var session = t.Sales.OpenSession("Sábado à noite", 5000, Evening);

            // --- first sale: drinks, dessert and food, paid in cash ------------
            var lookup = t.Catalog.GetCategories().ToDictionary(c => c.Id);
            var products = t.Catalog.GetProducts();

            var cart = new Cart();
            cart.Add(products.First(p => p.Name == "Cerveja"), 2);
            cart.Add(products.First(p => p.Name == "Bolo"));
            cart.Add(products.First(p => p.Name == "Bifana"));
            Assert.Equal(750, cart.TotalCents);

            var first = t.Sales.Save(
                SaleFactory.Build(cart, lookup, PaymentMethod.Cash, 1000, Evening.AddMinutes(31)), session.Id);

            Assert.Equal(1, first.TicketNumber);
            Assert.Equal(250, first.ChangeCents);

            // Bar list (Bolo) + two beer senhas + Cozinha list (Bifana).
            Assert.Equal(4, printer.Print(first));

            var slips = Directory.GetFiles(folder, "*.txt").Select(File.ReadAllText).ToList();
            Assert.Equal(4, slips.Count);
            Assert.Equal(2, slips.Count(s => s.Contains("CERVEJA") && s.Contains("#0001-")));
            Assert.Single(slips, s => s.Contains("BAR") && s.Contains("1x Bolo"));
            Assert.Single(slips, s => s.Contains("COZINHA") && s.Contains("1x Bifana"));

            // Nothing from the kitchen leaks onto the bar's slip, or the wrong
            // people get handed the wrong food all night.
            Assert.DoesNotContain(slips.Single(s => s.Contains("BAR") && s.Contains("Bolo")), "Bifana");

            // --- second sale: card ---------------------------------------------
            var cart2 = new Cart();
            cart2.Add(products.First(p => p.Name == "Sandes de Leitão"));
            var second = t.Sales.Save(
                SaleFactory.Build(cart2, lookup, PaymentMethod.Card, 0, Evening.AddMinutes(45)), session.Id);

            Assert.Equal(2, second.TicketNumber);

            // --- the organizer raises a price mid-event -------------------------
            var beerAgain = t.Catalog.GetProducts().First(p => p.Name == "Cerveja");
            beerAgain.PriceCents = 200;
            t.Catalog.UpdateProduct(beerAgain);
            Assert.Equal(18, beerAgain.StockQty);              // two sold

            // --- close the till -------------------------------------------------
            var reports = new ReportRepository(t.Db);
            var expected = 5000 + 750;
            t.Sales.CloseSession(session.Id, expected, Evening.AddHours(5));

            var report = reports.Build(t.Sales.GetSessions().Single(s => s.Id == session.Id));

            Assert.Equal(2, report.SalesCount);
            Assert.Equal(750, report.CashCents);
            Assert.Equal(400, report.CardCents);
            Assert.Equal(1150, report.TotalCents);
            Assert.Equal(expected, report.ExpectedCashCents);
            Assert.Equal(0, report.VarianceCents);

            // The earlier sale still reports at the price it was actually charged.
            Assert.Equal(300, report.Products.Single(p => p.Name == "Cerveja").TotalCents);
            Assert.Equal(2, report.Products.Single(p => p.Name == "Cerveja").Units);
            Assert.Equal(18, Assert.Single(report.Stock).Remaining);

            var csv = ReportRepository.ToCsv(report);
            Assert.Contains("Total;11,50", csv);
            Assert.Contains("Diferença;0,00", csv);

            // --- the night is recoverable ---------------------------------------
            var backup = t.Db.Backup(Evening.AddHours(5));
            Assert.True(File.Exists(backup));
            Assert.Equal(1150, new ReportRepository(new Db(backup))
                .Build(new SalesRepository(new Db(backup)).GetSessions().Single()).TotalCents);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_printer_that_is_not_there_does_not_cost_the_till_a_sale()
    {
        using var t = new TempDb();
        var session = t.Sales.OpenSession("Festa", 0, Evening);

        var cart = new Cart();
        cart.Add(t.Catalog.GetProducts().First(p => p.Name == "Cerveja"), 2);

        var sale = t.Sales.Save(
            SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                PaymentMethod.Cash, 300, Evening), session.Id);

        // Printing is what fails, and it fails after the money is already recorded.
        var printer = new TicketPrinter(new NetworkTransport("203.0.113.1:9100", timeoutMs: 300),
            new TicketOptions());
        Assert.ThrowsAny<Exception>(() => printer.Print(sale));

        Assert.Equal(300, new ReportRepository(t.Db)
            .Build(t.Sales.GetSessions().Single()).CashCents);
        Assert.Equal(sale.Id, t.Sales.GetLastSale(session.Id)!.Id);
    }
}
