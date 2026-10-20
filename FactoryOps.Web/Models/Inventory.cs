using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class InventoryStock : ICompanyRecord
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string CompanyKey { get; set; } = "fti-demo";
    public long OnHand { get; set; }
    public long ReorderPoint { get; set; }
    public int LeadTimeDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint Version { get; set; }
}

public class Replenishment : ICompanyRecord
{
    public int Id { get; set; }
    public string CompanyKey { get; set; } = "fti-demo";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public long Quantity { get; set; }
    public DateTime ExpectedDate { get; set; }
    [MaxLength(200)] public string Supplier { get; set; } = "";
    public int? SupplierId { get; set; }
    [MaxLength(100)] public string Reference { get; set; } = "";
    [MaxLength(1000)] public string Notes { get; set; } = "";
    [MaxLength(16)] public string Status { get; set; } = "Planned";
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid CreationToken { get; set; }
    public uint Version { get; set; }
}

public class StockMovement : ICompanyRecord
{
    public long Id { get; set; }
    public string CompanyKey { get; set; } = "fti-demo";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public long Quantity { get; set; }
    [MaxLength(32)] public string Kind { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public int? ReplenishmentId { get; set; }
    public Replenishment? Replenishment { get; set; }
    public Guid? RequestToken { get; set; }
}

public class StockReservation : ICompanyRecord
{
    public int WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string CompanyKey { get; set; } = "fti-demo";
    public long Quantity { get; set; }
    [MaxLength(32)] public string UnitOfMeasure { get; set; } = "";
    public DateTime NeedDate { get; set; }
    [MaxLength(16)] public string Status { get; set; } = "Held";
}
