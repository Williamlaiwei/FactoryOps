namespace FactoryOps.Web.Models;
public class WorkOrder
{
    public int Id { get;set;}
    public string Title {get;set;}="";
    
    public string ProductType {get;set;}="";

    public int Quantity {get; set;}

    public DateTime DueDate {get; set;}
    public string Status {get;set;}="Pending";
    // Customer relationship
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Construction site relationship
    public int? ConstructionSiteId { get; set; }
    public ConstructionSite? ConstructionSite { get; set; }
}