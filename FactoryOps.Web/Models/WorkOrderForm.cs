using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FactoryOps.Web.Models;

// Only editable values are bound; status, title and address snapshots stay server-owned.
public class WorkOrderForm
{
    public uint Version { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a valid customer.")]
    public int CustomerId { get; set; }
    public int? ConstructionSiteId { get; set; }
    [Required]
    public string DeliveryType { get; set; } = "Site";
    [Required(ErrorMessage = "Enter a valid requested delivery date.")]
    public DateTime? DueDate { get; set; }
    public string? DeliveryInstructions { get; set; }
    public List<WorkOrderItemForm> Items { get; set; } = new();

    [BindNever, ValidateNever]
    public Customer? SelectedCustomer { get; set; }
    [BindNever, ValidateNever]
    public List<ConstructionSite> AvailableSites { get; set; } = new();
}

public class WorkOrderItemForm
{
    public int? Id { get; set; }
    [Required(ErrorMessage = "Select a product for every item.")]
    public string ProductType { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Every quantity must be a positive whole number.")]
    public int Quantity { get; set; }
}
