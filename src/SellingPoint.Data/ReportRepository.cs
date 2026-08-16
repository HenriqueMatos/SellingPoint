using Dapper;
using SellingPoint.Core;

namespace SellingPoint.Data;

// Settable properties rather than positional records: SQLite's SUM() comes back as
// an Int64, and Dapper only narrows it to int when assigning a property.

public sealed record ProductSales
{
    public string Name { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int Units { get; set; }
    public int TotalCents { get; set; }
}

public sealed record CategorySales
{
    public string Name { get; set; } = "";
    public int Units { get; set; }
    public int TotalCents { get; set; }
}

public sealed record StockLine
{
    public string Name { get; set; } = "";
    public int Sold { get; set; }
    public int Adjusted { get; set; }
    public int Remaining { get; set; }
}

public sealed record SessionReport
{
    public required Session Session { get; init; }
    public required int SalesCount { get; init; }
    public required int CashCents { get; init; }
    public required int CardCents { get; init; }
    public required IReadOnlyList<ProductSales> Products { get; init; }
    public required IReadOnlyList<CategorySales> Categories { get; init; }
    public required IReadOnlyList<StockLine> Stock { get; init; }

    /// <summary>Cash in or out of the drawer without a sale, oldest first.</summary>
    public required IReadOnlyList<CashMovement> CashMovements { get; init; }

    public int TotalCents => CashCents + CardCents;

    /// <summary>Negative when more was taken out of the drawer than put in.</summary>
    public int CashMovementCents => CashMovements.Sum(m => m.Cents);

    /// <summary>
    /// What should be in the box: the opening float, plus every cash sale, less
    /// whatever was carried out of it during the night. Leave that last part out
    /// and the count below is measured against a number nobody expects to match.
    /// </summary>
    public int ExpectedCashCents => Session.OpeningFloatCents + CashCents + CashMovementCents;

    /// <summary>Positive means more cash than expected, negative means short.</summary>
    public int? VarianceCents => Session.ClosingCountedCents - ExpectedCashCents;
}

public sealed class ReportRepository(Db db)
{
    public SessionReport Build(Session session)
    {
        using var c = db.Open();
        var id = session.Id;

        var cash = c.ExecuteScalar<int>(
            "SELECT COALESCE(SUM(total_cents), 0) FROM sale WHERE session_id = @id AND payment_method = 'Cash'",
            new { id });
        var card = c.ExecuteScalar<int>(
            "SELECT COALESCE(SUM(total_cents), 0) FROM sale WHERE session_id = @id AND payment_method = 'Card'",
            new { id });
        var count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sale WHERE session_id = @id", new { id });

        // Grouped by the snapshotted names, so a product renamed or deleted after
        // the fact still reports under what it was actually sold as.
        var products = c.Query<ProductSales>(
            """
            SELECT sl.product_name AS Name, sl.category_name AS CategoryName,
                   SUM(sl.qty) AS Units, SUM(sl.line_total_cents) AS TotalCents
            FROM sale_line sl
            JOIN sale s ON s.id = sl.sale_id
            WHERE s.session_id = @id
            GROUP BY sl.product_name, sl.category_name
            ORDER BY TotalCents DESC
            """, new { id }).AsList();

        var categories = c.Query<CategorySales>(
            """
            SELECT sl.category_name AS Name, SUM(sl.qty) AS Units, SUM(sl.line_total_cents) AS TotalCents
            FROM sale_line sl
            JOIN sale s ON s.id = sl.sale_id
            WHERE s.session_id = @id
            GROUP BY sl.category_name
            ORDER BY TotalCents DESC
            """, new { id }).AsList();

        var stock = c.Query<StockLine>(
            """
            SELECT p.name AS Name,
                   COALESCE((SELECT SUM(sl.qty) FROM sale_line sl
                             JOIN sale s ON s.id = sl.sale_id
                             WHERE s.session_id = @id AND sl.product_id = p.id), 0) AS Sold,
                   COALESCE((SELECT SUM(a.delta) FROM stock_adjustment a
                             WHERE a.session_id = @id AND a.product_id = p.id), 0) AS Adjusted,
                   p.stock_qty AS Remaining
            FROM product p
            WHERE p.track_stock = 1
            ORDER BY p.sort_order, p.id
            """, new { id }).AsList();

        var movements = c.Query<CashMovement>(
            "SELECT * FROM cash_movement WHERE session_id = @id ORDER BY id", new { id }).AsList();

        return new SessionReport
        {
            Session = session,
            SalesCount = count,
            CashCents = cash,
            CardCents = card,
            Products = products,
            Categories = categories,
            Stock = stock,
            CashMovements = movements
        };
    }

    /// <summary>
    /// Semicolon separated with comma decimals - what Excel opens correctly on a
    /// Portuguese Windows without an import wizard.
    /// </summary>
    public static string ToCsv(SessionReport report)
    {
        var csv = new System.Text.StringBuilder();

        csv.AppendLine($"Sessão;{Escape(report.Session.Name)}");
        csv.AppendLine($"Aberta;{report.Session.OpenedAt:dd/MM/yyyy HH:mm}");
        csv.AppendLine($"Fechada;{report.Session.ClosedAt:dd/MM/yyyy HH:mm}");
        csv.AppendLine($"Vendas;{report.SalesCount}");
        csv.AppendLine($"Dinheiro;{Money.FormatPlain(report.CashCents)}");
        csv.AppendLine($"Cartão;{Money.FormatPlain(report.CardCents)}");
        csv.AppendLine($"Total;{Money.FormatPlain(report.TotalCents)}");
        csv.AppendLine($"Fundo de caixa;{Money.FormatPlain(report.Session.OpeningFloatCents)}");

        if (report.CashMovements.Count > 0)
            csv.AppendLine($"Sangrias e reforços;{Money.FormatPlain(report.CashMovementCents)}");

        csv.AppendLine($"Dinheiro esperado;{Money.FormatPlain(report.ExpectedCashCents)}");

        if (report.Session.ClosingCountedCents is { } counted)
        {
            csv.AppendLine($"Dinheiro contado;{Money.FormatPlain(counted)}");
            csv.AppendLine($"Diferença;{Money.FormatPlain(report.VarianceCents ?? 0)}");
        }

        csv.AppendLine();
        csv.AppendLine("Produto;Categoria;Unidades;Total");
        foreach (var p in report.Products)
            csv.AppendLine($"{Escape(p.Name)};{Escape(p.CategoryName)};{p.Units};{Money.FormatPlain(p.TotalCents)}");

        if (report.CashMovements.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("Hora;Movimento;Valor");
            foreach (var m in report.CashMovements)
            {
                csv.AppendLine($"{m.CreatedAt:dd/MM HH:mm};{Escape(m.Reason)};{Money.FormatPlain(m.Cents)}");
            }
        }

        csv.AppendLine();
        csv.AppendLine("Categoria;Unidades;Total");
        foreach (var c in report.Categories)
            csv.AppendLine($"{Escape(c.Name)};{c.Units};{Money.FormatPlain(c.TotalCents)}");

        if (report.Stock.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("Produto;Vendido;Reposto;Em stock");
            foreach (var s in report.Stock)
                csv.AppendLine($"{Escape(s.Name)};{s.Sold};{s.Adjusted};{s.Remaining}");
        }

        return csv.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(';') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
