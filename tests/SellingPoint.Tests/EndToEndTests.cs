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
                Header = "FESTA DA ALDEIA 2026", Footer = "Obrigado!"
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

            // One ticket per category: two beer senhas + a Sobremesas list + a
            // Comida list. The customer hands each slip to the counter that serves it.
            Assert.Equal(4, printer.Print(first));

            var slips = Directory.GetFiles(folder, "*.txt").Select(File.ReadAllText).ToList();
            Assert.Equal(4, slips.Count);
            Assert.Equal(2, slips.Count(s => s.Contains("CERVEJA") && s.Contains("#0001-")));
            Assert.Single(slips, s => s.Contains("SOBREMESAS") && s.Contains("1x Bolo"));
            Assert.Single(slips, s => s.Contains("COMIDA") && s.Contains("1x Bifana"));

            // Every slip carries the same ticket number, so three pieces of paper
            // are still recognisably one order.
            Assert.All(slips, s => Assert.Contains("#0001", s));

            // Nothing crosses over: the dessert stand must not be handed a bifana.
            Assert.DoesNotContain(slips.Single(s => s.Contains("1x Bolo")), "Bifana");
            Assert.DoesNotContain(slips.Single(s => s.Contains("1x Bifana")), "Bolo");

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
    public void Two_categories_sharing_a_print_group_come_out_on_one_slip()
    {
        // Separation is the default; combining is what the organizer opts into by
        // giving two categories the same group name in Gestão. This is that path.
        using var t = new TempDb();
        var folder = Path.Combine(Path.GetTempPath(), $"sellingpoint-shared-{Guid.NewGuid():N}");

        try
        {
            var sobremesas = t.Catalog.GetCategories().Single(c => c.Name == "Sobremesas");
            sobremesas.PrintGroup = "Bebidas";          // desserts now print with the drinks
            t.Catalog.UpdateCategory(sobremesas);

            var session = t.Sales.OpenSession("Festa", 0, Evening);
            var products = t.Catalog.GetProducts();
            var cart = new Cart();
            cart.Add(products.First(p => p.Name == "Cerveja"));
            cart.Add(products.First(p => p.Name == "Bolo"));
            cart.Add(products.First(p => p.Name == "Bifana"));

            var sale = t.Sales.Save(
                SaleFactory.Build(cart, t.Catalog.GetCategories().ToDictionary(c => c.Id),
                    PaymentMethod.Cash, 1000, Evening), session.Id);

            var printer = new TicketPrinter(new FileTransport(folder), new TicketOptions());

            // Two slips now, not three: drinks and desserts share one.
            Assert.Equal(2, printer.Print(sale));

            var slips = Directory.GetFiles(folder, "*.txt").Select(File.ReadAllText).ToList();
            var shared = Assert.Single(slips, s => s.Contains("1x Cerveja"));
            Assert.Contains("1x Bolo", shared);
            Assert.Single(slips, s => s.Contains("COMIDA") && s.Contains("1x Bifana"));
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
