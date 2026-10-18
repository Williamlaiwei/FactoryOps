using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class ProductForm
{
    public uint Version { get; set; }
    [Required, StringLength(64)]
    [RegularExpression(@"[A-Za-z0-9][A-Za-z0-9._/\-]*", ErrorMessage = "SKU must contain letters, numbers, dots, slashes, hyphens or underscores.")]
    public string Sku { get; set; } = "";
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; }
    [StringLength(100)] public string? Category { get; set; }
    [Required, StringLength(32)] public string UnitOfMeasure { get; set; } = "each";
    [StringLength(500)] public string? Dimensions { get; set; }
    [StringLength(4000)] public string? Specifications { get; set; }
    [StringLength(1000)] public string? Packaging { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(20000)] public string? Aliases { get; set; }
}

public class ProductIndexViewModel
{
    public string? Q { get; set; }
    public string Status { get; set; } = "all";
    public List<Product> Products { get; set; } = new();
}

public class ProductImportForm
{
    [Required(ErrorMessage = "Choose a CSV file to import.")]
    public IFormFile? File { get; set; }
}
