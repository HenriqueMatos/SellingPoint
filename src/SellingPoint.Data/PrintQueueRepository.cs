using Dapper;

namespace SellingPoint.Data;

public sealed class PrintJob
{
    public int Id { get; set; }
    public int? SaleId { get; set; }

    /// <summary>What the operator sees in the queue: "Talão #0042 — BAR".</summary>
    public string Title { get; set; } = "";

    /// <summary>The encoded ESC/POS bytes, ready to go down the wire as they are.</summary>
    public byte[] Payload { get; set; } = [];

    public string Preview { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Slips that have been paid for but not yet printed. Nothing is ever dropped
/// because a printer was unplugged; it waits here until one answers.
/// </summary>
public sealed class PrintQueueRepository(Db db)
{
    private const string InsertSql =
        """
        INSERT INTO print_job(sale_id, title, payload, preview, created_at)
        VALUES(@SaleId, @Title, @Payload, @Preview, @CreatedAt);
        SELECT last_insert_rowid();
        """;

    /// <summary>
    /// Every slip of one sale, in a single transaction. They used to be committed
    /// one at a time, and on the till's own thread: an order that prints three
    /// slips meant three separate commits between the customer paying and the
    /// screen coming back. The order they go in is the order they come out.
    /// </summary>
    public void Enqueue(IReadOnlyList<PrintJob> jobs)
    {
        if (jobs.Count == 0) return;

        using var c = db.Open();
        using var tx = c.BeginTransaction();

        foreach (var job in jobs) job.Id = c.ExecuteScalar<int>(InsertSql, job, tx);

        tx.Commit();
    }

    /// <summary>Oldest first, so a queue that drains prints in the order it was rung up.</summary>
    public PrintJob? NextPending()
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<PrintJob>(
            "SELECT * FROM print_job WHERE printed_at IS NULL ORDER BY id LIMIT 1");
    }

    public List<PrintJob> Pending(int limit = 100)
    {
        using var c = db.Open();
        return c.Query<PrintJob>(
            "SELECT * FROM print_job WHERE printed_at IS NULL ORDER BY id LIMIT @limit",
            new { limit }).AsList();
    }

    public int PendingCount()
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM print_job WHERE printed_at IS NULL");
    }

    public void MarkPrinted(int id, DateTime now)
    {
        using var c = db.Open();
        c.Execute("UPDATE print_job SET printed_at = @now, last_error = NULL WHERE id = @id", new { id, now });
    }

    public void MarkFailed(int id, string error)
    {
        using var c = db.Open();
        c.Execute("UPDATE print_job SET attempts = attempts + 1, last_error = @error WHERE id = @id",
            new { id, error });
    }

    public int DiscardPending()
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM print_job WHERE printed_at IS NULL");
    }

    /// <summary>Housekeeping: printed slips are only kept as a trail for a few days.</summary>
    public int PurgePrintedBefore(DateTime cutoff)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM print_job WHERE printed_at IS NOT NULL AND printed_at < @cutoff",
            new { cutoff });
    }
}
