namespace FactoryOps.Web.Models;

public class ConstructionSite
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public string SiteName { get; set; } = "";

    public string Address { get; set; } = "";

    public string Suburb { get; set; } = "";

    public string State { get; set; } = "NSW";

    public string Postcode { get; set; } = "";

    public string ContactName { get; set; } = "";

    public string ContactPhone { get; set; } = "";
    public string Status { get; set; } = "Active";

    public DateTime? CompletedDate { get; set; }
    public List<WorkOrder> WorkOrders { get; set; } = new();
}