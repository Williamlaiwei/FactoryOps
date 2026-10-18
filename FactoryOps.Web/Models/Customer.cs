namespace FactoryOps.Web.Models;

public class Customer : ICompanyRecord
{
    public string CompanyKey { get; set; } = "fti-demo";
    public int Id { get; set; }

    public string CompanyName { get; set; } = "";

    public string ContactName { get; set; } = "";

    public string Phone { get; set; } = "";

    public string Email { get; set; } = "";

    public string BillingAddress { get; set; } = "";

    public string Notes { get; set; } = "";

    // One customer can have multiple construction sites
    public List<ConstructionSite> ConstructionSites { get; set; } = new();

    // One customer can have multiple work orders
    public List<WorkOrder> WorkOrders { get; set; } = new();
}
