using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

namespace FactoryOps.Web.Controllers;

public partial class InventoryController(FactoryOpsDbContext db, InventoryPlanner planner, InventoryService inventory, BusinessClock clock, ILogger<InventoryController> logger, IDataProtectionProvider protection, CompanyContext company) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var plan = await planner.Build();
        if (!string.IsNullOrWhiteSpace(q)) plan.Products = plan.Products.Where(x =>
            x.Product.Sku.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase) ||
            x.Product.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        ViewData["Query"] = q;
        return View(plan);
    }

    [HttpGet]
    public async Task<IActionResult> Calendar() => View(await planner.Build());

    [HttpGet]
    public async Task<IActionResult> BulkCount(string? q, bool untrackedOnly = false, int page = 1)
    {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim().ToLower();
            query = query.Where(x => x.Sku.ToLower().Contains(search) || x.Name.ToLower().Contains(search));
        }
        if (untrackedOnly) query = query.Where(x => !db.InventoryStocks.Any(s => s.ProductId == x.Id));
        var model = new BulkStockForm { Q = q, UntrackedOnly = untrackedOnly, TotalProducts = await query.CountAsync() };
        model.Page = Math.Clamp(page, 1, model.TotalPages);
        var products = await query.OrderBy(x => x.Sku).ThenBy(x => x.Id)
            .Skip((model.Page - 1) * BulkStockForm.PageSize).Take(BulkStockForm.PageSize).ToListAsync();
        var ids = products.Select(x => x.Id).ToArray();
        var stocks = await db.InventoryStocks.AsNoTracking().Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId);
        model.Rows = products.Select(product =>
        {
            stocks.TryGetValue(product.Id, out var stock);
            return new BulkStockRow { ProductId = product.Id, ProductVersion = product.Version, Product = product,
                Version = stock?.Version, CurrentOnHand = stock?.OnHand,
                ReorderPoint = stock?.ReorderPoint ?? 0, LeadTimeDays = stock?.LeadTimeDays ?? 0 };
        }).ToList();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCount(BulkStockForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        var ids = model.Rows.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var stocks = await db.InventoryStocks.Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId);
        var selected = model.Rows.Where(x => x.Selected).ToList();
        if (selected.Count == 0) ModelState.AddModelError("", "Select at least one product to save.");
        if (ids.Length != model.Rows.Count) ModelState.AddModelError("", "Each product can appear only once. Reload this page.");
        foreach (var row in model.Rows)
        {
            products.TryGetValue(row.ProductId, out var product);
            stocks.TryGetValue(row.ProductId, out var stock);
            row.Product = product; row.CurrentOnHand = stock?.OnHand;
            if (!row.Selected) continue;
            var label = product?.Sku ?? $"Product #{row.ProductId}";
            if (product == null || product.Version != row.ProductVersion || stock?.Version != row.Version)
                ModelState.AddModelError("", $"{label}: product or stock changed. Reload before saving; nothing was changed.");
            if (stock == null && !row.CountedQuantity.HasValue)
                ModelState.AddModelError("", $"{label}: enter an opening physical count, including zero if counted empty.");
            if (stock != null && row.CountedQuantity.HasValue && row.CountedQuantity != stock.OnHand && string.IsNullOrWhiteSpace(model.Reason))
                ModelState.AddModelError(nameof(model.Reason), $"{label}: enter a count reason when changing existing stock.");
        }
        if (!ModelState.IsValid) return View(model);
        foreach (var row in selected)
        {
            var product = products[row.ProductId];
            stocks.TryGetValue(row.ProductId, out var stock);
            ApplyStockCount(product, stock, row.CountedQuantity, row.ReorderPoint, row.LeadTimeDays, model.Reason);
        }
        if (!await Save()) return View(model);
        await transaction.CommitAsync();
        TempData["InventoryMessage"] = $"Saved {selected.Count} product(s). Physical counts replace on-hand balances; existing holds are unchanged. Review any shortage warnings.";
        return RedirectToAction(nameof(Index));
    }

    private void ApplyStockCount(Product product, InventoryStock? stock, long? count, long reorderPoint, int leadTimeDays, string? reason)
    {
        var opening = stock == null;
        var delta = count.HasValue ? count.Value - (stock?.OnHand ?? 0) : 0;
        if (stock == null)
        {
            stock = new InventoryStock { ProductId = product.Id }; db.InventoryStocks.Add(stock);
            // Protect the ordering unit against a simultaneous catalog edit.
            db.Entry(product).Property(x => x.UnitOfMeasure).IsModified = true;
        }
        stock.OnHand = count ?? stock.OnHand;
        stock.ReorderPoint = reorderPoint; stock.LeadTimeDays = leadTimeDays;
        stock.UpdatedAt = DateTimeOffset.UtcNow;
        // Every selected row advances its version, including settings-only saves.
        db.Entry(stock).Property(x => x.UpdatedAt).IsModified = true;
        if (delta != 0) db.StockMovements.Add(new()
        {
            ProductId = product.Id, Quantity = delta, Kind = opening ? "Opening balance" : "Stock count",
            Reason = string.IsNullOrWhiteSpace(reason) ? "Opening physical count (bulk)" : reason.Trim()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var plan = (await planner.Build()).Products.SingleOrDefault(x => x.Product.Id == id);
        if (plan == null) return NotFound();
        return View(new InventoryProductPage
        {
            Plan = plan, Movements = await db.StockMovements.AsNoTracking().Where(x => x.ProductId == id)
                .OrderByDescending(x => x.Id).Take(200).ToListAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Configure(int id)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        var stock = await db.InventoryStocks.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == id);
        SetStockView(product, stock);
        return View(new StockSettingsForm { Version = stock?.Version, ReorderPoint = stock?.ReorderPoint ?? 0,
            LeadTimeDays = stock?.LeadTimeDays ?? 0, RequestToken = Guid.NewGuid() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(int id, StockSettingsForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (product == null) return NotFound();
        var stock = await db.InventoryStocks.SingleOrDefaultAsync(x => x.ProductId == id);
        SetStockView(product, stock);
        if (model.RequestToken.HasValue && await db.StockMovements.AnyAsync(x => x.RequestToken == model.RequestToken))
        { TempData["InventoryMessage"] = "This stock count was already saved. No duplicate adjustment was made."; return RedirectToAction(nameof(Details), new { id }); }
        if (stock?.Version != model.Version) ModelState.AddModelError("", "Stock changed since you opened this form. Reload it before saving.");
        if (stock == null && !model.CountedQuantity.HasValue) ModelState.AddModelError(nameof(model.CountedQuantity), "Enter an opening physical stock count to start tracking.");
        var delta = model.CountedQuantity.HasValue ? model.CountedQuantity.Value - (stock?.OnHand ?? 0) : 0;
        if (stock != null && delta != 0 && string.IsNullOrWhiteSpace(model.Reason))
            ModelState.AddModelError(nameof(model.Reason), "Enter a reason for changing the physical stock count.");
        if (!ModelState.IsValid) return View(model);
        var opening = stock == null;
        if (stock == null)
        {
            stock = new InventoryStock { ProductId = id }; db.InventoryStocks.Add(stock);
            // Starting a count and changing a product's ordering unit must not race.
            db.Attach(product);
            db.Entry(product).Property(x => x.UnitOfMeasure).IsModified = true;
        }
        stock.OnHand = model.CountedQuantity ?? stock.OnHand;
        stock.ReorderPoint = model.ReorderPoint;
        stock.LeadTimeDays = model.LeadTimeDays;
        stock.UpdatedAt = DateTimeOffset.UtcNow;
        if (delta != 0) db.StockMovements.Add(new()
        {
            ProductId = id, Quantity = delta, Kind = opening ? "Opening balance" : "Stock count",
            Reason = model.Reason?.Trim() ?? "Opening physical count", RequestToken = model.RequestToken
        });
        if (!await Save()) return View(model);
        await transaction.CommitAsync();
        TempData["InventoryMessage"] = "Stock settings and actual count saved. Existing outbound holds remain reserved; review any shortage warnings.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Replenishments() => View(await db.Replenishments.AsNoTracking().Include(x => x.Product)
        .OrderBy(x => x.Status == "Planned" ? 0 : 1).ThenBy(x => x.ExpectedDate).ThenBy(x => x.Id).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> Schedule(int? productId)
    {
        var model = new ReplenishmentForm { ProductId = productId ?? 0, ExpectedDate = clock.Today, CreationToken = Guid.NewGuid() };
        model.SupplierId = await db.ProductSuppliers.Where(x => x.ProductId == model.ProductId && x.IsDefault && x.Supplier.IsActive)
            .Select(x => (int?)x.SupplierId).SingleOrDefaultAsync();
        await LoadReplenishmentForm(model);
        return View("ReplenishmentForm", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Schedule(ReplenishmentForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        if (model.CreationToken.HasValue && await db.Replenishments.AnyAsync(x => x.CreationToken == model.CreationToken))
        { TempData["InventoryMessage"] = "This replenishment was already scheduled. No duplicate was added."; return RedirectToAction(nameof(Replenishments)); }
        await ValidateReplenishment(model);
        if (!ModelState.IsValid) { await LoadReplenishmentForm(model); return View("ReplenishmentForm", model); }
        var item = new Replenishment { CreationToken = model.CreationToken!.Value };
        Apply(item, model);
        db.Replenishments.Add(item);
        if (!await Save()) { await LoadReplenishmentForm(model); return View("ReplenishmentForm", model); }
        await transaction.CommitAsync();
        TempData["InventoryMessage"] = "Incoming hold scheduled. It can cover orders from its expected arrival date. Actual stock increases only on receipt.";
        return RedirectToAction(nameof(Replenishments));
    }

    [HttpGet]
    public async Task<IActionResult> EditReplenishment(int id)
    {
        var item = await db.Replenishments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (item.Status != "Planned") return RedirectToAction(nameof(Replenishments));
        ViewData["ReplenishmentId"] = id;
        var model = new ReplenishmentForm
        {
            ProductId = item.ProductId, Quantity = item.Quantity, ExpectedDate = item.ExpectedDate,
            SupplierId = item.SupplierId, Supplier = item.Supplier, Reference = item.Reference, Notes = item.Notes,
            Version = item.Version, CreationToken = item.CreationToken
        };
        await LoadReplenishmentForm(model, item.ProductId);
        return View("ReplenishmentForm", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditReplenishment(int id, ReplenishmentForm model)
    {
        await using var transaction = await inventory.BeginMutation();
        var item = await db.Replenishments.SingleOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        ViewData["ReplenishmentId"] = id;
        if (item.Status != "Planned" || item.Version != model.Version || item.CreationToken != model.CreationToken)
            ModelState.AddModelError("", "This replenishment changed. Reload the schedule before editing it.");
        await ValidateReplenishment(model, item);
        if (!ModelState.IsValid) { await LoadReplenishmentForm(model, item.ProductId); return View("ReplenishmentForm", model); }
        Apply(item, model);
        // Force an update even when all form values are unchanged, so replayed forms become stale.
        db.Entry(item).Property(x => x.Notes).IsModified = true;
        if (!await Save()) { await LoadReplenishmentForm(model, item.ProductId); return View("ReplenishmentForm", model); }
        await transaction.CommitAsync();
        TempData["InventoryMessage"] = "Incoming hold updated. Existing orders retain their holds; review Stock Schedule for any commitments now at risk.";
        return RedirectToAction(nameof(Replenishments));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Receive(int id, uint? version) => ChangeReplenishment(id, version, true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, uint? version) => ChangeReplenishment(id, version, false);

    private async Task<IActionResult> ChangeReplenishment(int id, uint? version, bool receive)
    {
        await using var transaction = await inventory.BeginMutation();
        var item = await db.Replenishments.SingleOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (!ModelState.IsValid || version != item.Version || item.Status != "Planned")
        { TempData["InventoryMessage"] = "This replenishment changed or was already processed. Reload the schedule; no additional stock was added."; return RedirectToAction(nameof(Replenishments)); }
        if (receive)
        {
            var stock = await db.InventoryStocks.SingleOrDefaultAsync(x => x.ProductId == item.ProductId);
            if (stock == null || stock.OnHand + item.Quantity > StockSettingsForm.MaximumQuantity)
            { TempData["InventoryMessage"] = "Stock is not tracked or the resulting quantity is too large. Review inventory first."; return RedirectToAction(nameof(Replenishments)); }
            stock.OnHand += item.Quantity; stock.UpdatedAt = DateTimeOffset.UtcNow;
            item.Status = "Received"; item.ReceivedAt = DateTimeOffset.UtcNow;
            db.StockMovements.Add(new() { ProductId = item.ProductId, Quantity = item.Quantity, Kind = "Receipt",
                ReplenishmentId = item.Id, Reason = $"Received replenishment R-{item.Id}" });
        }
        else item.Status = "Cancelled";
        if (!await Save()) TempData["InventoryMessage"] = "The replenishment or stock changed. Nothing was saved. Reload and retry.";
        else
        {
            await transaction.CommitAsync();
            TempData["InventoryMessage"] = receive ? "Receipt recorded: incoming hold released and actual stock increased once." : "Incoming hold cancelled. Existing outbound holds remain; review any supply shortages.";
        }
        return RedirectToAction(nameof(Replenishments));
    }

    private async Task ValidateReplenishment(ReplenishmentForm model, Replenishment? original = null)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.ProductId);
        if (product == null || (!product.IsActive && original?.ProductId != model.ProductId) ||
            !await db.InventoryStocks.AnyAsync(x => x.ProductId == model.ProductId))
            ModelState.AddModelError(nameof(model.ProductId), "Select a product with stock tracking enabled. Start tracking it in Inventory first.");
        if (!model.ExpectedDate.HasValue || model.ExpectedDate.Value == default ||
            (model.ExpectedDate.Value.Date < clock.Today && model.ExpectedDate.Value.Date != original?.ExpectedDate.Date))
            ModelState.AddModelError(nameof(model.ExpectedDate), "Choose today or a future expected arrival date.");
        if (model.SupplierId.HasValue)
        {
            var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.SupplierId);
            var unchanged = original?.SupplierId == model.SupplierId && original?.ProductId == model.ProductId;
            if (supplier == null || (!unchanged && (!supplier.IsActive || !await db.ProductSuppliers.AnyAsync(x =>
                    x.ProductId == model.ProductId && x.SupplierId == model.SupplierId))))
                ModelState.AddModelError(nameof(model.SupplierId), "Select an active supplier linked to this product, or enter a supplier manually.");
            else model.Supplier = unchanged ? original!.Supplier : supplier.Name;
        }
    }

    private async Task LoadReplenishmentForm(ReplenishmentForm model, int? originalProduct = null)
    {
        await LoadProducts(originalProduct);
        var options = await db.ProductSuppliers.AsNoTracking().Where(x => x.ProductId == model.ProductId && x.Supplier.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Supplier.Name)
            .Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            { Value = x.SupplierId.ToString(), Text = x.Supplier.Name + (x.IsDefault ? " (Default)" : "") }).ToListAsync();
        if (model.SupplierId.HasValue && options.All(x => x.Value != model.SupplierId.Value.ToString()))
        {
            var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.SupplierId);
            if (supplier != null) options.Add(new() { Value = supplier.Id.ToString(), Text = model.Supplier + " (Recorded supplier)" });
        }
        ViewData["SupplierOptions"] = options;
    }

    private async Task LoadProducts(int? selected = null) => ViewData["Products"] = await db.Products.AsNoTracking()
        .Where(x => (x.IsActive || x.Id == selected) && db.InventoryStocks.Any(s => s.ProductId == x.Id)).OrderBy(x => x.Sku).ToListAsync();

    private void SetStockView(Product product, InventoryStock? stock)
    { ViewData["Product"] = product; ViewData["Stock"] = stock; }

    private static void Apply(Replenishment item, ReplenishmentForm model)
    {
        item.ProductId = model.ProductId; item.Quantity = model.Quantity; item.ExpectedDate = model.ExpectedDate!.Value.Date;
        item.SupplierId = model.SupplierId;
        item.Supplier = model.Supplier?.Trim() ?? ""; item.Reference = model.Reference?.Trim() ?? ""; item.Notes = model.Notes?.Trim() ?? "";
    }

    private async Task<bool> Save()
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException)
        {
            if (db.Database.CurrentTransaction != null) await db.Database.CurrentTransaction.RollbackAsync();
            ModelState.AddModelError("", "Stock or replenishment changed. Reload before saving; nothing was changed.");
        }
        catch (DbUpdateException error)
        {
            if (db.Database.CurrentTransaction != null) await db.Database.CurrentTransaction.RollbackAsync();
            logger.LogWarning(error, "Inventory save failed"); ModelState.AddModelError("", "Inventory could not be saved. No partial changes were made. Reload and retry.");
        }
        return false;
    }
}
