namespace FactoryOps.Web.Models;

public class WorkOrderItem
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public string ProductType { get; set; } = "";
    public int Quantity { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductSkuSnapshot { get; set; } = "";
    public string UnitOfMeasureSnapshot { get; set; } = "";
}
