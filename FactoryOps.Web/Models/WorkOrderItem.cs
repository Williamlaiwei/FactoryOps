namespace FactoryOps.Web.Models;

public class WorkOrderItem
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public string ProductType { get; set; } = "";
    public int Quantity { get; set; }
}
