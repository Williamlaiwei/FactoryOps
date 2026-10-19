namespace FactoryOps.Web.Models;

public class WorkOrder : ICompanyRecord
{
    public string CompanyKey { get; set; } = "fti-demo";
    public int Id { get; set; }
    public string Title { get; set; } = "";

    public string ProductType { get; set; } = "";

    public int Quantity { get; set; }

    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public string FulfillmentStatus { get; set; } = "Unconfirmed";
    public DateTimeOffset? ShippedAt { get; set; }
    public Guid? CreationToken { get; set; }
    // Customer relationship
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string DeliveryType { get; set; } = "Site";

    public string DeliveryAddressSnapshot { get; set; } = "";

    public string DeliveryInstructions { get; set; } = "";
    // Construction site relationship
    public int? ConstructionSiteId { get; set; }
    public ConstructionSite? ConstructionSite { get; set; }
    public List<WorkOrderItem> Items { get; set; } = new();
    // PostgreSQL's built-in xmin column; no schema addition is required.
    public uint Version { get; set; }
}
