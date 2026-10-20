using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class StockSettingsForm : IValidatableObject
{
    public const long MaximumQuantity = 1_000_000_000_000;
    public uint? Version { get; set; }
    [Range(0, MaximumQuantity)] public long? CountedQuantity { get; set; }
    [Range(0, MaximumQuantity)] public long ReorderPoint { get; set; }
    [Range(0, 3650)] public int LeadTimeDays { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    [Required] public Guid? RequestToken { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestToken == Guid.Empty)
            yield return new("Reload this stock form before saving.", new[] { nameof(RequestToken) });
    }
}

public class ReplenishmentForm : IValidatableObject
{
    public uint Version { get; set; }
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, StockSettingsForm.MaximumQuantity)] public long Quantity { get; set; } = 1;
    [Required(ErrorMessage = "Enter an expected arrival date.")] public DateTime? ExpectedDate { get; set; }
    [StringLength(200)] public string? Supplier { get; set; }
    [Range(1, int.MaxValue)] public int? SupplierId { get; set; }
    [StringLength(100)] public string? Reference { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    [Required] public Guid? CreationToken { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreationToken == Guid.Empty)
            yield return new("Reload this replenishment form before saving.", new[] { nameof(CreationToken) });
    }
}

public class InventoryProductPage
{
    public InventoryProductPlan Plan { get; set; } = null!;
    public List<StockMovement> Movements { get; set; } = new();
}

public class InventoryPlan
{
    public DateTime Today { get; set; }
    public List<InventoryProductPlan> Products { get; set; } = new();
    public Dictionary<int, List<string>> OrderWarnings { get; set; } = new();
    public List<InventoryEvent> Calendar => Products.SelectMany(x => x.Events)
        .OrderBy(x => x.Date).ThenBy(x => x.Product.Sku).ThenBy(x => x.Outgoing > 0 ? 1 : 0).ThenBy(x => x.SourceId).ToList();
}

public class InventoryProductPlan
{
    public Product Product { get; set; } = null!;
    public InventoryStock? Stock { get; set; }
    public List<InventoryEvent> Events { get; set; } = new();
    public int UncertainDemand { get; set; }
    public long MinimumBalance { get; set; }
    public DateTime? FirstShortageDate { get; set; }
    public DateTime? ReorderBy { get; set; }
    public bool DependsOnReceipts { get; set; }
    public bool HasDashboardRisk => FirstShortageDate.HasValue || UncertainDemand > 0 || DependsOnReceipts ||
        (Stock != null && (Stock.OnHand == 0 || MinimumBalance <= Stock.ReorderPoint || OverdueReceipts > 0));
    public string DashboardAlert => Alert == "Sufficient" && DependsOnReceipts ? "Depends on incoming stock" : Alert;
    public long ReorderQuantity => Stock == null ? 0 : Math.Max(0, Stock.ReorderPoint - MinimumBalance + 1);
    public long OpenDemand => Events.Sum(x => x.Outgoing);
    public long ExpectedInbound => Events.Where(x => x.CountsInProjection).Sum(x => x.Incoming);
    public int OverdueReceipts => Events.Count(x => x.Incoming > 0 && !x.CountsInProjection);
    public string Alert => Stock == null ? "Not tracked" : FirstShortageDate != null ? "Projected shortage" :
        UncertainDemand > 0 ? "Review required" : Stock.OnHand == 0 ? "Out of stock" :
        MinimumBalance <= Stock.ReorderPoint ? "Reorder needed" : OverdueReceipts > 0 ? "Receipt overdue" : "Sufficient";
}

public class InventoryEvent
{
    public Product Product { get; set; } = null!;
    public DateTime Date { get; set; }
    public DateTime ScheduledDate { get; set; }
    public string Kind { get; set; } = "";
    public int SourceId { get; set; }
    public long Incoming { get; set; }
    public long Outgoing { get; set; }
    public bool CountsInProjection { get; set; } = true;
    public bool ProjectionKnown { get; set; } = true;
    public long Balance { get; set; }
}
