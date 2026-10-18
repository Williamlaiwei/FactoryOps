using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

public class ProductsController(FactoryOpsDbContext db, ProductCatalog catalog, ILogger<ProductsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string status = "all")
    {
        var query = db.Products.AsNoTracking().Include(x => x.Aliases).AsQueryable();
        if (status == "active") query = query.Where(x => x.IsActive);
        else if (status == "inactive") query = query.Where(x => !x.IsActive);
        else status = "all";
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(x => EF.Functions.ILike(x.Sku, pattern, "\\") ||
                EF.Functions.ILike(x.Name, pattern, "\\") || EF.Functions.ILike(x.Category, pattern, "\\") ||
                EF.Functions.ILike(x.Dimensions, pattern, "\\") || EF.Functions.ILike(x.PackingSize, pattern, "\\") ||
                EF.Functions.ILike(x.RoughIn, pattern, "\\") || EF.Functions.ILike(x.HeatingMethod, pattern, "\\") ||
                EF.Functions.ILike(x.FlushingMethod, pattern, "\\") || EF.Functions.ILike(x.CeramicBody, pattern, "\\") ||
                EF.Functions.ILike(x.Color, pattern, "\\") || EF.Functions.ILike(x.DisplayMode, pattern, "\\") ||
                EF.Functions.ILike(x.NetWeight, pattern, "\\") || EF.Functions.ILike(x.GrossWeight, pattern, "\\") ||
                EF.Functions.ILike(x.Description, pattern, "\\") || EF.Functions.ILike(x.Specifications, pattern, "\\") ||
                EF.Functions.ILike(x.Packaging, pattern, "\\") ||
                x.Aliases.Any(a => EF.Functions.ILike(a.Value, pattern, "\\")));
        }
        return View(new ProductIndexViewModel { Q = q, Status = status, Products = await query.OrderBy(x => x.Name).ThenBy(x => x.Sku).ToListAsync() });
    }

    [HttpGet]
    public IActionResult Create() => View(new ProductForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductForm model)
    {
        await ValidateProduct(model);
        if (!ModelState.IsValid) return View(model);
        var product = new Product();
        ProductCatalog.Apply(product, model);
        db.Products.Add(product);
        if (!await SaveProduct()) return View(model);
        TempData["ProductMessage"] = $"Product {product.Sku} created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products.AsNoTracking().Include(x => x.Aliases).SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        ViewData["ProductSource"] = product.SourceUrl;
        ViewData["ProductIsDemo"] = product.IsDemo;
        return View(new ProductForm
        {
            Version = product.Version, Sku = product.Sku, Name = product.Name, Description = product.Description,
            Category = product.Category, UnitOfMeasure = product.UnitOfMeasure, Dimensions = product.Dimensions,
            PackingSize = product.PackingSize, RoughIn = product.RoughIn, HeatingMethod = product.HeatingMethod,
            FlushingMethod = product.FlushingMethod, CeramicBody = product.CeramicBody, Color = product.Color,
            DisplayMode = product.DisplayMode, NetWeight = product.NetWeight, GrossWeight = product.GrossWeight,
            Specifications = product.Specifications, Packaging = product.Packaging, IsActive = product.IsActive,
            Aliases = string.Join(Environment.NewLine, product.Aliases.OrderBy(x => x.Value).Select(x => x.Value))
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductForm model)
    {
        var product = await db.Products.Include(x => x.Aliases).SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        await ValidateProduct(model, id);
        if (model.Version != product.Version)
            ModelState.AddModelError("", "This product has changed. Reload the product before saving.");
        if (!ModelState.IsValid) return View(model);
        ProductCatalog.Apply(product, model);
        db.Entry(product).Property(x => x.Name).IsModified = true;
        db.Entry(product).Property(x => x.Version).OriginalValue = model.Version;
        if (!await SaveProduct()) return View(model);
        TempData["ProductMessage"] = $"Product {product.Sku} updated. Historical order details are preserved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        ViewData["IsUsed"] = await db.WorkOrderItems.AnyAsync(x => x.ProductId == id);
        return View(product);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, uint version)
    {
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        if (version != product.Version || await db.WorkOrderItems.AnyAsync(x => x.ProductId == id))
            TempData["ProductMessage"] = "The product changed or is used by an order. Reload it or deactivate it instead.";
        else
        {
            db.Products.Remove(product);
            try
            {
                await db.SaveChangesAsync();
                TempData["ProductMessage"] = $"Product {product.Sku} deleted.";
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Product {ProductId} could not be deleted", id);
                TempData["ProductMessage"] = "The product changed or is now used by an order. Deactivate it instead.";
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Json(Array.Empty<object>());
        if (q.Trim().Length > 200) return BadRequest("Search text must be 200 characters or fewer.");
        var matches = await catalog.Match(q);
        return Json(matches.Select(x => new { x.Id, x.Sku, x.Name, x.UnitOfMeasure, x.IsDemo, x.SkuIsVerified, x.SourceUrl }));
    }

    [HttpGet]
    public IActionResult Import() => View(new ProductImportForm());

    [HttpGet]
    public IActionResult CsvTemplate() => File(CsvImportExamples.Download(CsvImportExamples.Products),
        "text/csv; charset=utf-8", "products-template.csv");

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Import(ProductImportForm model)
    {
        if (model.File != null && (model.File.Length == 0 || model.File.Length > 5 * 1024 * 1024 ||
            !Path.GetExtension(model.File.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(model.File), "Choose a nonempty CSV file no larger than 5 MB.");
        if (!ModelState.IsValid) return View(model);

        var forms = new List<ProductForm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(model.File!.OpenReadStream(), new UTF8Encoding(false, true), true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim, DetectColumnCountChanges = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
        });
        try
        {
            if (!await csv.ReadAsync()) ModelState.AddModelError("", "The CSV is empty.");
            else
            {
                csv.ReadHeader();
                var headers = csv.HeaderRecord ?? Array.Empty<string>();
                if (headers.Select(x => x.Trim().ToLowerInvariant()).Distinct().Count() != headers.Length)
                    ModelState.AddModelError("", "CSV column names must be unique.");
                foreach (var required in new[] { "Sku", "Name", "UnitOfMeasure" })
                    if (!headers.Any(x => x.Trim().Equals(required, StringComparison.OrdinalIgnoreCase)))
                        ModelState.AddModelError("", $"Missing required CSV column: {required}.");
                if (!ModelState.IsValid) return View(model);
                string Field(string name) => csv.TryGetField<string>(name, out var value) ? value ?? "" : "";
                while (await csv.ReadAsync())
                {
                    if (forms.Count >= 1000) { ModelState.AddModelError("", "Maximum 1000 product rows per import."); break; }
                    var row = csv.Parser.Row;
                    var activeText = Field("IsActive");
                    var form = new ProductForm
                    {
                        Sku = Field("Sku"), Name = Field("Name"), UnitOfMeasure = Field("UnitOfMeasure"),
                        Description = Field("Description"), Category = Field("Category"), Dimensions = Field("Dimensions"),
                        PackingSize = Field("PackingSize"), RoughIn = Field("RoughIn"), HeatingMethod = Field("HeatingMethod"),
                        FlushingMethod = Field("FlushingMethod"), CeramicBody = Field("CeramicBody"), Color = Field("Color"),
                        DisplayMode = Field("DisplayMode"), NetWeight = Field("NetWeight"), GrossWeight = Field("GrossWeight"),
                        Specifications = Field("Specifications"), Packaging = Field("Packaging"), Aliases = Field("Aliases"),
                        IsActive = string.IsNullOrWhiteSpace(activeText) || activeText.Equals("true", StringComparison.OrdinalIgnoreCase)
                    };
                    if (!string.IsNullOrWhiteSpace(activeText) && !bool.TryParse(activeText, out _))
                        ModelState.AddModelError("", $"Row {row}: IsActive must be true or false.");
                    foreach (var error in await catalog.Validate(form))
                        ModelState.AddModelError("", $"Row {row}: {error.ErrorMessage}");
                    if (!seen.Add(form.Sku)) ModelState.AddModelError("", $"Row {row}: duplicate SKU within this file.");
                    forms.Add(form);
                    if (ModelState.ErrorCount >= 50) { ModelState.AddModelError("", "Too many errors. Correct these rows and retry."); break; }
                }
            }
        }
        catch (Exception ex) when (ex is CsvHelperException or DecoderFallbackException)
        {
            ModelState.AddModelError("", $"CSV row {csv.Parser.Row} could not be read. Use UTF-8 CSV with consistent columns and correctly quoted values.");
        }
        if (forms.Count == 0) ModelState.AddModelError("", "The CSV must contain at least one product row.");
        if (!ModelState.IsValid) return View(model);
        foreach (var form in forms)
        {
            var product = new Product();
            ProductCatalog.Apply(product, form);
            db.Products.Add(product);
        }
        // One SaveChanges transaction: a rejected row or uniqueness race imports nothing.
        if (!await SaveProduct()) return View(model);
        TempData["ProductMessage"] = $"Imported {forms.Count} product(s). Existing products were not overwritten.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateProduct(ProductForm model, int? excludingId = null)
    {
        // Revalidate normalized string fields while retaining binding errors for
        // version numbers and booleans.
        ModelState.Remove(nameof(model.Sku));
        ModelState.Remove(nameof(model.Name));
        ModelState.Remove(nameof(model.UnitOfMeasure));
        foreach (var error in await catalog.Validate(model, excludingId))
            ModelState.AddModelError(error.MemberNames.FirstOrDefault() ?? "", error.ErrorMessage!);
    }

    private async Task<bool> SaveProduct()
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError("", "This product changed while saving. Reload it before trying again.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Product catalog save failed");
            ModelState.AddModelError("", "No products were saved. A SKU may already exist; check your data and try again.");
        }
        return false;
    }
}
