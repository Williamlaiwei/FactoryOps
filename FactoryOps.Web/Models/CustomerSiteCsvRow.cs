
namespace FactoryOps.Web.Models;

public class CustomerSiteCsvRow
{
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string BillingAddress { get; set; } = "";

    public string SiteName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Suburb { get; set; } = "";
    public string State { get; set; } = "NSW";
    public string Postcode { get; set; } = "";
    public string SiteContactName { get; set; } = "";
    public string SiteContactPhone { get; set; } = "";
}
