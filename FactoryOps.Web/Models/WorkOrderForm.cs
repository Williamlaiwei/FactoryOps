using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FactoryOps.Web.Models;

// Only editable values are bound; status, title and address snapshots stay server-owned.
public class WorkOrderForm
{
    public uint Version { get; set; }
    public Guid? CreationToken { get; set; }
    public bool ConfirmCurrentUnits { get; set; }
    public bool AcceptStockWarning { get; set; }
    public string? StockWarningToken { get; set; }
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
    [BindNever, ValidateNever]
    public List<Product> AvailableProducts { get; set; } = new();
}

public class WorkOrderItemForm
{
    public int? Id { get; set; }
    public int? ProductId { get; set; }
    // Accept original form clients by exact catalog name; new forms use ProductId.
    public string? ProductType { get; set; }
    [BindNever, ValidateNever]
    public Product? SelectedProduct { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Every quantity must be a positive whole number.")]
    public int Quantity { get; set; }
}
