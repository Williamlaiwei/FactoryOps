using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SuppliersController(FactoryOpsDbContext db, InventoryService inventory, CompanyContext company,
    ILogger<SuppliersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Products"] = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync();
        return View(await db.Suppliers.AsNoTracking().OrderBy(x => x.Name).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Product(int id)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        var links = await db.ProductSuppliers.AsNoTracking().Include(x => x.Supplier)
            .Where(x => x.ProductId == id).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Supplier.Name).ToListAsync();
        var ids = links.Select(x => x.SupplierId).ToArray();
        return View(new ProductSuppliersPage { Product = product, Links = links,
            Available = await db.Suppliers.AsNoTracking().Where(x => x.IsActive && !ids.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> ForProduct(int productId)
    {
        if (!await db.Products.AnyAsync(x => x.Id == productId)) return NotFound();
        var suppliers = await db.ProductSuppliers.AsNoTracking().Where(x => x.ProductId == productId && x.Supplier.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Supplier.Name)
            .Select(x => new { id = x.SupplierId, name = x.Supplier.Name, isDefault = x.IsDefault }).ToListAsync();
        return Json(new { companyKey = company.Key, suppliers });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? productId)
    {
        if (productId.HasValue && !await db.Products.AnyAsync(x => x.Id == productId)) return NotFound();
        return View("Form", new SupplierForm { ProductId = productId, MakeDefault = productId.HasValue,
            CreationToken = Guid.NewGuid() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplierForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        if (model.ProductId.HasValue && !await db.Products.AnyAsync(x => x.Id == model.ProductId)) return NotFound();
        if (model.CreationToken.HasValue && await db.Suppliers.AnyAsync(x => x.CreationToken == model.CreationToken))
        { TempData["SupplierMessage"] = "This supplier was already created. No duplicate was added."; return ReturnTo(model.ProductId); }
        await Validate(model);
        if (model.ProductId.HasValue && !model.IsActive)
            ModelState.AddModelError(nameof(model.IsActive), "Activate the supplier before linking it to a product.");
        if (!ModelState.IsValid) return View("Form", model);
        var supplier = new Supplier { CreationToken = model.CreationToken!.Value };
        Apply(supplier, model); db.Suppliers.Add(supplier);
        if (model.ProductId.HasValue)
        {
            if (model.MakeDefault) await ClearDefaults(model.ProductId.Value);
            db.ProductSuppliers.Add(new() { ProductId = model.ProductId.Value, Supplier = supplier, IsDefault = model.MakeDefault });
        }
        if (!await Save()) return View("Form", model);
        await transaction.CommitAsync();
        TempData["SupplierMessage"] = "Supplier created. Product defaults apply to new replenishments; existing records keep their recorded supplier.";
        return ReturnTo(model.ProductId);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, int? productId)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (supplier == null || (productId.HasValue && !await db.Products.AnyAsync(x => x.Id == productId))) return NotFound();
        ViewData["SupplierId"] = id;
        return View("Form", new SupplierForm { Name = supplier.Name, ContactName = supplier.ContactName,
            Email = supplier.Email, Phone = supplier.Phone, Notes = supplier.Notes, IsActive = supplier.IsActive,
            CreationToken = supplier.CreationToken, Version = supplier.Version, ProductId = productId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SupplierForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == id);
        if (supplier == null || (model.ProductId.HasValue && !await db.Products.AnyAsync(x => x.Id == model.ProductId))) return NotFound();
        ViewData["SupplierId"] = id;
        if (supplier.Version != model.Version || supplier.CreationToken != model.CreationToken)
            ModelState.AddModelError("", "This supplier changed. Reload before saving.");
        await Validate(model, id);
        if (!ModelState.IsValid) return View("Form", model);
        Apply(supplier, model);
        db.Entry(supplier).Property(x => x.Name).IsModified = true;
        if (!supplier.IsActive)
            await db.ProductSuppliers.Where(x => x.SupplierId == id && x.IsDefault).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsDefault, false));
        if (!await Save()) return View("Form", model);
        await transaction.CommitAsync();
        TempData["SupplierMessage"] = "Supplier updated. Existing replenishment names are preserved. Inactive suppliers are removed from product defaults.";
        return ReturnTo(model.ProductId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int productId, int supplierId, bool makeDefault)
    {
        await using var transaction = await inventory.BeginMutation();
        if (!await db.Products.AnyAsync(x => x.Id == productId)) return NotFound();
        if (!ModelState.IsValid || !await db.Suppliers.AnyAsync(x => x.Id == supplierId && x.IsActive))
            TempData["SupplierMessage"] = "Select an active supplier from this company.";
        else if (await db.ProductSuppliers.AnyAsync(x => x.ProductId == productId && x.SupplierId == supplierId))
            TempData["SupplierMessage"] = "This supplier is already linked. No duplicate was added.";
        else
        {
            if (makeDefault) await ClearDefaults(productId);
            db.ProductSuppliers.Add(new() { ProductId = productId, SupplierId = supplierId, IsDefault = makeDefault });
            if (await Save()) { await transaction.CommitAsync(); TempData["SupplierMessage"] = "Supplier linked to this product."; }
            else TempData["SupplierMessage"] = "Supplier could not be linked. Nothing was changed; reload and retry.";
        }
        return ReturnTo(productId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int productId, int supplierId, uint? version) => await ChangeLink(productId, supplierId, version, false);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId, int supplierId, uint? version) => await ChangeLink(productId, supplierId, version, true);

    private async Task<IActionResult> ChangeLink(int productId, int supplierId, uint? version, bool remove)
    {
        await using var transaction = await inventory.BeginMutation();
        var link = await db.ProductSuppliers.Include(x => x.Supplier).SingleOrDefaultAsync(x => x.ProductId == productId && x.SupplierId == supplierId);
        if (link == null) return NotFound();
        if (!ModelState.IsValid || link.Version != version || (!remove && !link.Supplier.IsActive))
            TempData["SupplierMessage"] = "This product supplier changed or is inactive. Reload before trying again.";
        else
        {
            if (remove) db.ProductSuppliers.Remove(link);
            else
            {
                await db.ProductSuppliers.Where(x => x.ProductId == productId && x.SupplierId != supplierId && x.IsDefault)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.IsDefault, false));
                link.IsDefault = true; db.Entry(link).Property(x => x.IsDefault).IsModified = true;
            }
            if (await Save()) { await transaction.CommitAsync(); TempData["SupplierMessage"] = remove ? "Supplier unlinked. Existing replenishments are preserved." : "Default supplier updated for new replenishments."; }
            else TempData["SupplierMessage"] = "The supplier link changed. Nothing was saved; reload and retry.";
        }
        return ReturnTo(productId);
    }

    private Task<int> ClearDefaults(int productId) => db.ProductSuppliers.Where(x => x.ProductId == productId && x.IsDefault)
        .ExecuteUpdateAsync(x => x.SetProperty(p => p.IsDefault, false));

    private IActionResult ReturnTo(int? productId) => productId.HasValue ? RedirectToAction(nameof(Product), new { id = productId.Value }) : RedirectToAction(nameof(Index));

    private async Task Validate(SupplierForm model, int? id = null)
    {
        if (model.CreationToken == Guid.Empty) ModelState.AddModelError(nameof(model.CreationToken), "Reload the supplier form before saving.");
        var normalized = (model.Name ?? "").Trim().ToUpperInvariant();
        if (await db.Suppliers.AnyAsync(x => x.Id != id && x.NormalizedName == normalized))
            ModelState.AddModelError(nameof(model.Name), "A supplier with this name already exists in this company.");
    }

    private static void Apply(Supplier supplier, SupplierForm model)
    {
        supplier.Name = model.Name.Trim(); supplier.NormalizedName = supplier.Name.ToUpperInvariant();
        supplier.ContactName = model.ContactName?.Trim() ?? ""; supplier.Email = model.Email?.Trim() ?? "";
        supplier.Phone = model.Phone?.Trim() ?? ""; supplier.Notes = model.Notes?.Trim() ?? ""; supplier.IsActive = model.IsActive;
    }

    private async Task<bool> Save()
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException error)
        {
            if (db.Database.CurrentTransaction != null) await db.Database.CurrentTransaction.RollbackAsync();
            logger.LogWarning(error, "Supplier save failed");
            ModelState.AddModelError("", "Supplier changed or could not be saved. No partial changes were made; reload and retry.");
            return false;
        }
    }
}
