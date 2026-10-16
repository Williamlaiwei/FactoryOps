namespace FactoryOps.Web.Models;
public class WorkOrder
{
    public int Id { get;set;}
    public string Title {get;set;}="";
    
    public string ProductType {get;set;}="";

    public int Quantity {get; set;}

    public DateTime DueDate {get; set;}
    public string Status {get;set;}="Pending";

}