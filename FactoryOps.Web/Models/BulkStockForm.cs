using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FactoryOps.Web.Models;

public class BulkStockForm
{
    public const int PageSize = 50;
    public string? Q { get; set; }
    public bool UntrackedOnly { get; set; }
    public int Page { get; set; } = 1;
    [StringLength(500)] public string? Reason { get; set; }
    [MinLength(1), MaxLength(PageSize)] public List<BulkStockRow> Rows { get; set; } = new();
    [BindNever] public int TotalProducts { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalProducts / (double)PageSize));
}

public class BulkStockRow
{
    public bool Selected { get; set; }
    public int ProductId { get; set; }
    public uint ProductVersion { get; set; }
    public uint? Version { get; set; }
    [Range(0, StockSettingsForm.MaximumQuantity)] public long? CountedQuantity { get; set; }
    [Range(0, StockSettingsForm.MaximumQuantity)] public long ReorderPoint { get; set; }
    [Range(0, 3650)] public int LeadTimeDays { get; set; }
    [BindNever] public Product? Product { get; set; }
    [BindNever] public long? CurrentOnHand { get; set; }
}
