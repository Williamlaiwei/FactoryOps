using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class Product : ICompanyRecord
{
    public string CompanyKey { get; set; } = "fti-demo";
    [MaxLength(500)] public string SourceUrl { get; set; } = "";
    public bool IsDemo { get; set; }
    public bool SkuIsVerified { get; set; } = true;
    public int Id { get; set; }
    [MaxLength(64)] public string Sku { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(4000)] public string Description { get; set; } = "";
    [MaxLength(100)] public string Category { get; set; } = "";
    [MaxLength(32)] public string UnitOfMeasure { get; set; } = "";
    [MaxLength(500)] public string Dimensions { get; set; } = "";
    [MaxLength(500)] public string PackingSize { get; set; } = "";
    [MaxLength(300)] public string RoughIn { get; set; } = "";
    [MaxLength(200)] public string HeatingMethod { get; set; } = "";
    [MaxLength(200)] public string FlushingMethod { get; set; } = "";
    [MaxLength(300)] public string CeramicBody { get; set; } = "";
    [MaxLength(300)] public string Color { get; set; } = "";
    [MaxLength(200)] public string DisplayMode { get; set; } = "";
    [MaxLength(100)] public string NetWeight { get; set; } = "";
    [MaxLength(100)] public string GrossWeight { get; set; } = "";
    [MaxLength(4000)] public string Specifications { get; set; } = "";
    [MaxLength(1000)] public string Packaging { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }
    public List<ProductAlias> Aliases { get; set; } = new();
}

public class ProductAlias
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [MaxLength(200)] public string Value { get; set; } = "";
    [MaxLength(200)] public string NormalizedValue { get; set; } = "";
}
