using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class Supplier : ICompanyRecord
{
    public int Id { get; set; }
    public string CompanyKey { get; set; } = "fti-demo";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(200)] public string NormalizedName { get; set; } = "";
    [MaxLength(150)] public string ContactName { get; set; } = "";
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(50)] public string Phone { get; set; } = "";
    [MaxLength(1000)] public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid CreationToken { get; set; }
    public uint Version { get; set; }
}

public class ProductSupplier : ICompanyRecord
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string CompanyKey { get; set; } = "fti-demo";
    public bool IsDefault { get; set; }
    public uint Version { get; set; }
}

public class SupplierForm
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(150)] public string? ContactName { get; set; }
    [EmailAddress, StringLength(254)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(1, int.MaxValue)] public int? ProductId { get; set; }
    public bool MakeDefault { get; set; }
    [Required] public Guid? CreationToken { get; set; }
    public uint Version { get; set; }
}

public class ProductSuppliersPage
{
    public Product Product { get; set; } = null!;
    public List<ProductSupplier> Links { get; set; } = new();
    public List<Supplier> Available { get; set; } = new();
}
