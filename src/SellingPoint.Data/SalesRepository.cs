using Dapper;
using SellingPoint.Core;

namespace SellingPoint.Data;

/// <summary>Sessions and the sales recorded inside them.</summary>
public sealed class SalesRepository(Db db)
{
    public Session? GetOpenSession()
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Session>(
            "SELECT * FROM session WHERE closed_at IS NULL ORDER BY id DESC LIMIT 1");
    }

    public List<Session> GetSessions()
    {
        using var c = db.Open();
        return c.Query<Session>("SELECT * FROM session ORDER BY id DESC").AsList();
    }

    public Session OpenSession(string name, int openingFloatCents, DateTime now)
    {
        if (GetOpenSession() is { } existing)
            throw new InvalidOperationException($"Session '{existing.Name}' is still open.");

        using var c = db.Open();
        var id = c.ExecuteScalar<int>(
            """
            INSERT INTO session(name, opened_at, opening_float_cents)
            VALUES(@name, @now, @openingFloatCents);
            SELECT last_insert_rowid();
            """,
            new { name, now, openingFloatCents });

        return new Session { Id = id, Name = name, OpenedAt = now, OpeningFloatCents = openingFloatCents };
    }

    public void CloseSession(int sessionId, int countedCents, DateTime now)
    {
        using var c = db.Open();
        c.Execute(
            "UPDATE session SET closed_at = @now, closing_counted_cents = @countedCents WHERE id = @sessionId",
            new { now, countedCents, sessionId });
    }

    /// <summary>
    /// Persists the sale, assigns its per-session ticket number, and decrements
    /// stock - all in one transaction, so a crash mid-write cannot leave stock
    /// counted down against a sale that was never recorded.
    /// </summary>
    public Sale Save(Sale sale, int sessionId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();

        sale.SessionId = sessionId;
        sale.TicketNumber = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(ticket_number), 0) + 1 FROM sale WHERE session_id = @sessionId",
            new { sessionId }, tx);

        sale.Id = c.ExecuteScalar<int>(
            """
            INSERT INTO sale(session_id, ticket_number, created_at, total_cents,
                             payment_method, cash_received_cents, change_cents)
            VALUES(@SessionId, @TicketNumber, @CreatedAt, @TotalCents,
                   @PaymentMethod, @CashReceivedCents, @ChangeCents);
            SELECT last_insert_rowid();
            """,
            new
            {
                sale.SessionId, sale.TicketNumber, sale.CreatedAt, sale.TotalCents,
                PaymentMethod = sale.PaymentMethod.ToString(), sale.CashReceivedCents, sale.ChangeCents
            }, tx);

        foreach (var line in sale.Lines)
        {
            line.SaleId = sale.Id;
            line.Id = c.ExecuteScalar<int>(
                """
                INSERT INTO sale_line(sale_id, product_id, product_name, unit_price_cents,
                                      category_name, print_group, slip_mode, qty, line_total_cents)
                VALUES(@SaleId, @ProductId, @ProductName, @UnitPriceCents,
                       @CategoryName, @PrintGroup, @SlipMode, @Qty, @LineTotalCents);
                SELECT last_insert_rowid();
                """,
                new
                {
                    line.SaleId, line.ProductId, line.ProductName, line.UnitPriceCents,
                    line.CategoryName, line.PrintGroup, SlipMode = line.SlipMode.ToString(),
                    line.Qty, line.LineTotalCents
                }, tx);

            c.Execute(
                "UPDATE product SET stock_qty = stock_qty - @Qty WHERE id = @ProductId AND track_stock = 1",
                new { line.Qty, line.ProductId }, tx);
        }

        tx.Commit();
        return sale;
    }

    public Sale? GetSale(int saleId)
    {
        using var c = db.Open();
        var sale = c.QuerySingleOrDefault<Sale>("SELECT * FROM sale WHERE id = @saleId", new { saleId });
        if (sale is null) return null;

        sale.Lines = c.Query<SaleLine>(
            "SELECT * FROM sale_line WHERE sale_id = @saleId ORDER BY id", new { saleId }).AsList();
        return sale;
    }

    /// <summary>Backs the "reprint last ticket" button.</summary>
    public Sale? GetLastSale(int sessionId)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<int?>(
            "SELECT id FROM sale WHERE session_id = @sessionId ORDER BY id DESC LIMIT 1",
            new { sessionId });

        return id is null ? null : GetSale(id.Value);
    }
}
