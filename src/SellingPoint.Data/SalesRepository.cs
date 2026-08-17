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

    // --- festivals ---------------------------------------------------------
    // A festival holds the nights of one event. One is open at a time, which falls
    // out of only one session being open at a time.

    public List<Event> GetEvents()
    {
        using var c = db.Open();
        return c.Query<Event>("SELECT * FROM event ORDER BY id DESC").AsList();
    }

    public Event? GetOpenEvent()
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Event>(
            "SELECT * FROM event WHERE closed_at IS NULL ORDER BY id DESC LIMIT 1");
    }

    public Event OpenEvent(string name, DateTime now)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<int>(
            """
            INSERT INTO event(name, created_at) VALUES(@name, @now);
            SELECT last_insert_rowid();
            """, new { name, now });

        return new Event { Id = id, Name = name, CreatedAt = now };
    }

    /// <summary>
    /// Ends a festival. Refuses while one of its nights is still open, because the
    /// night's takings are not counted until it closes and the festival's total
    /// would be short by them.
    /// </summary>
    public void CloseEvent(int eventId, DateTime now)
    {
        using var c = db.Open();

        var openNight = c.ExecuteScalar<string?>(
            "SELECT name FROM session WHERE event_id = @eventId AND closed_at IS NULL LIMIT 1",
            new { eventId });

        if (openNight is not null)
            throw new InvalidOperationException($"A sessão '{openNight}' ainda está aberta.");

        c.Execute("UPDATE event SET closed_at = @now WHERE id = @eventId", new { now, eventId });
    }

    /// <summary>
    /// Removes a festival from this machine: its nights, its sales, the lines of
    /// those sales, the slips, the cash movements and the stock adjustments made
    /// during it. Products, prices, categories and settings stay, so the till is
    /// ready for the next festival without being set up again.
    ///
    /// Written out by hand and in order rather than left to the database. Sales
    /// reference a session with no ON DELETE CASCADE, so deleting the session first
    /// fails the constraint; print_job and stock_adjustment carry an id with no
    /// foreign key at all, so nothing would remove them. For something this
    /// destructive, being able to read the order is worth more than being clever.
    ///
    /// Refuses while a night is still open: that night's takings are not counted
    /// yet, and it would be deleting a till somebody is standing at.
    /// </summary>
    public void DeleteEvent(int eventId)
    {
        using var c = db.Open();

        var openNight = c.ExecuteScalar<string?>(
            "SELECT name FROM session WHERE event_id = @eventId AND closed_at IS NULL LIMIT 1",
            new { eventId });

        if (openNight is not null)
            throw new InvalidOperationException($"A sessão '{openNight}' ainda está aberta.");

        using var tx = c.BeginTransaction();
        const string nights = "SELECT id FROM session WHERE event_id = @eventId";

        c.Execute($"DELETE FROM print_job WHERE sale_id IN (SELECT id FROM sale WHERE session_id IN ({nights}))",
            new { eventId }, tx);

        // sale_line goes with its sale, by cascade.
        c.Execute($"DELETE FROM sale WHERE session_id IN ({nights})", new { eventId }, tx);
        c.Execute($"DELETE FROM cash_movement WHERE session_id IN ({nights})", new { eventId }, tx);
        c.Execute($"DELETE FROM stock_adjustment WHERE session_id IN ({nights})", new { eventId }, tx);
        c.Execute("DELETE FROM session WHERE event_id = @eventId", new { eventId }, tx);
        c.Execute("DELETE FROM event WHERE id = @eventId", new { eventId }, tx);

        tx.Commit();
    }

    public void RenameEvent(int eventId, string name)
    {
        using var c = db.Open();
        c.Execute("UPDATE event SET name = @name WHERE id = @eventId", new { name, eventId });
    }

    public List<Session> GetSessions(int eventId)
    {
        using var c = db.Open();
        return c.Query<Session>(
            "SELECT * FROM session WHERE event_id = @eventId ORDER BY id DESC",
            new { eventId }).AsList();
    }

    /// <summary>
    /// Opens a night inside a festival.
    ///
    /// With no event given it joins the open one, and starts one named after the
    /// night if there is none. The invariant - that no session exists outside a
    /// festival - is kept here rather than trusted to every caller.
    /// </summary>
    public Session OpenSession(string name, int openingFloatCents, DateTime now, int? eventId = null)
    {
        if (GetOpenSession() is { } existing)
            throw new InvalidOperationException($"Session '{existing.Name}' is still open.");

        eventId ??= GetOpenEvent()?.Id ?? OpenEvent(name, now).Id;

        using var c = db.Open();
        var id = c.ExecuteScalar<int>(
            """
            INSERT INTO session(event_id, name, opened_at, opening_float_cents)
            VALUES(@eventId, @name, @now, @openingFloatCents);
            SELECT last_insert_rowid();
            """,
            new { eventId, name, now, openingFloatCents });

        return new Session
        {
            Id = id, EventId = eventId, Name = name,
            OpenedAt = now, OpeningFloatCents = openingFloatCents
        };
    }

    /// <summary>
    /// Records cash leaving or entering the drawer without a sale. Negative takes
    /// money out. Kept as its own row rather than adjusting the opening float, so
    /// the closing report can show what happened and when.
    /// </summary>
    public CashMovement RecordCashMovement(int sessionId, int cents, string reason, DateTime now)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<int>(
            """
            INSERT INTO cash_movement(session_id, cents, reason, created_at)
            VALUES(@sessionId, @cents, @reason, @now);
            SELECT last_insert_rowid();
            """,
            new { sessionId, cents, reason, now });

        return new CashMovement
        {
            Id = id, SessionId = sessionId, Cents = cents, Reason = reason, CreatedAt = now
        };
    }

    public List<CashMovement> GetCashMovements(int sessionId)
    {
        using var c = db.Open();
        return c.Query<CashMovement>(
            "SELECT * FROM cash_movement WHERE session_id = @sessionId ORDER BY id",
            new { sessionId }).AsList();
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

    /// <summary>
    /// The sale a customer is holding the slip for. Reprint only ever reached the
    /// last one, so a slip from an hour ago could not be looked at at all: the
    /// volunteer either refused someone who was in the right or gave away food to
    /// someone who was not.
    /// </summary>
    public Sale? GetSaleByTicket(int sessionId, int ticketNumber)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<int?>(
            "SELECT id FROM sale WHERE session_id = @sessionId AND ticket_number = @ticketNumber",
            new { sessionId, ticketNumber });

        return id is null ? null : GetSale(id.Value);
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
