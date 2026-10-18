using System.ComponentModel.DataAnnotations;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Services;

// Company-local structured catalog. Document retrieval and AI belong outside this service.
public class ProductCatalog(FactoryOpsDbContext db)
{
    public static void Normalize(ProductForm form)
    {
        form.Sku = (form.Sku ?? "").Trim().ToUpperInvariant();
        form.Name = (form.Name ?? "").Trim();
        form.UnitOfMeasure = (form.UnitOfMeasure ?? "").Trim();
        form.Description = form.Description?.Trim();
        form.Category = form.Category?.Trim();
        form.Dimensions = form.Dimensions?.Trim();
        form.Specifications = form.Specifications?.Trim();
        form.Packaging = form.Packaging?.Trim();
    }

    public static List<string> AliasValues(string? text) => (text ?? "")
        .Split(new[] { '\r', '\n', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<List<ValidationResult>> Validate(ProductForm form, int? excludingId = null)
    {
        Normalize(form);
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), errors, true);
        var aliases = AliasValues(form.Aliases);
        if (aliases.Count > 100 || aliases.Any(x => x.Length > 200))
            errors.Add(new("Use at most 100 aliases, each no longer than 200 characters.", new[] { nameof(form.Aliases) }));
        if (await db.Products.AnyAsync(x => x.Sku == form.Sku && x.Id != excludingId))
            errors.Add(new("This SKU already exists. Use a unique product code.", new[] { nameof(form.Sku) }));
        return errors;
    }

    public static void Apply(Product product, ProductForm form)
    {
        product.Sku = form.Sku;
        product.Name = form.Name;
        product.Description = form.Description ?? "";
        product.Category = form.Category ?? "";
        product.UnitOfMeasure = form.UnitOfMeasure;
        product.Dimensions = form.Dimensions ?? "";
        product.Specifications = form.Specifications ?? "";
        product.Packaging = form.Packaging ?? "";
        product.IsActive = form.IsActive;
        var values = AliasValues(form.Aliases);
        var normalized = values.Select(x => x.ToUpperInvariant()).ToHashSet();
        foreach (var removed in product.Aliases.Where(x => !normalized.Contains(x.NormalizedValue)).ToList())
            product.Aliases.Remove(removed);
        foreach (var value in values)
        {
            var key = value.ToUpperInvariant();
            var alias = product.Aliases.SingleOrDefault(x => x.NormalizedValue == key);
            if (alias == null) product.Aliases.Add(new() { Value = value, NormalizedValue = key });
            else alias.Value = value;
        }
    }

    public async Task<List<Product>> Match(string text)
    {
        var key = text.Trim().ToUpperInvariant();
        var exactSku = await db.Products.AsNoTracking().Where(x => x.IsActive && x.Sku == key).ToListAsync();
        if (exactSku.Count > 0) return exactSku;
        // An alias may be shared. Return every candidate rather than guessing.
        return await db.Products.AsNoTracking().Where(x => x.IsActive &&
            (x.Name.ToUpper() == key || x.Aliases.Any(a => a.NormalizedValue == key)))
            .OrderBy(x => x.Sku).ToListAsync();
    }
}
