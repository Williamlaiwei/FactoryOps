using System.ComponentModel.DataAnnotations;

namespace FactoryOps.Web.Models;

public class StockImportForm
{
    [Required(ErrorMessage = "Choose a stock CSV file.")]
    public IFormFile? File { get; set; }
}

public class StockImportPreview
{
    public string Token { get; set; } = "";
    public List<StockImportRow> Rows { get; set; } = new();
}

// These values are protected by Data Protection between preview and confirmation.
public class StockImportBatch
{
    public string CompanyKey { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public List<StockImportRow> Rows { get; set; } = new();
}

public class StockImportRow
{
    public int RowNumber { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public long? CountedQuantity { get; set; }
    public long? ReorderPoint { get; set; }
    public int? LeadTimeDays { get; set; }
    public string Reason { get; set; } = "";
    public string SourceSnapshot { get; set; } = "";
    public int ProductId { get; set; }
    public uint ProductVersion { get; set; }
    public uint? StockVersion { get; set; }
    public long? PreviousOnHand { get; set; }
    public long ResolvedReorderPoint { get; set; }
    public int ResolvedLeadTimeDays { get; set; }
}

public class StockExportSnapshot
{
    public string CompanyKey { get; set; } = "";
    public int ProductId { get; set; }
    public uint ProductVersion { get; set; }
    public uint StockVersion { get; set; }
}
