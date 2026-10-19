namespace FactoryOps.Web.Models;

public class InventoryDashboard
{
    public List<Product> Products { get; set; } = new();
    public int SelectedProductId { get; set; }
    public int Months { get; set; } = 1;
    public StockProjection? Projection { get; set; }
    public int RiskProducts { get; set; }
    public int UnconfirmedOrders { get; set; }
}

public class StockProjection
{
    public string CompanyKey { get; set; } = "";
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int Months { get; set; }
    public long? OnHand { get; set; }
    public long OutboundHeld { get; set; }
    public long IncomingHeld { get; set; }
    public int OverdueReceipts { get; set; }
    public bool NeedsReview { get; set; }
    public List<StockProjectionDay> Days { get; set; } = new();
}

public class StockProjectionDay
{
    public string Date { get; set; } = "";
    public long Actual { get; set; }
    public long WithIncoming { get; set; }
    public long Available { get; set; }
    public long Incoming { get; set; }
    public long Outgoing { get; set; }
}
