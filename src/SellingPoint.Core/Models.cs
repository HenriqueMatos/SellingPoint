namespace SellingPoint.Core;

/// <summary>
/// A group of products that share a colour and a printing destination.
/// Categories with the same <see cref="PrintGroup"/> print together on one slip;
/// different groups print separate slips.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3A7BD5";
    public int SortOrder { get; set; }

    /// <summary>Free text, e.g. "Bar" or "Cozinha". Shared value = shared slip.</summary>
    public string PrintGroup { get; set; } = "Bar";

    public SlipMode SlipMode { get; set; } = SlipMode.Grouped;
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public int PriceCents { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool TrackStock { get; set; }
    public int StockQty { get; set; }
}

/// <summary>One event or one night. Ticket numbers restart at 1 with each session.</summary>
public class Session
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int OpeningFloatCents { get; set; }
    public int? ClosingCountedCents { get; set; }

    public bool IsOpen => ClosedAt is null;
}

public class Sale
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int TicketNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCents { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public int CashReceivedCents { get; set; }
    public int ChangeCents { get; set; }

    public List<SaleLine> Lines { get; set; } = [];
}

/// <summary>
/// The name, price, category and print settings are copied onto the line when the
/// sale is made. Raising the beer price at 23:00 must not rewrite what the 22:00
/// ticket charged, nor what last night's report says was sold.
/// </summary>
public class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int UnitPriceCents { get; set; }
    public string CategoryName { get; set; } = "";
    public string PrintGroup { get; set; } = "";
    public SlipMode SlipMode { get; set; }
    public int Qty { get; set; }
    public int LineTotalCents { get; set; }
}

/// <summary>
/// A manual stock change - a restock mid-event, or a correction. Sales are not
/// logged here; they are already derivable from <see cref="SaleLine"/>.
/// </summary>
public class StockAdjustment
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Delta { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? SessionId { get; set; }
}
