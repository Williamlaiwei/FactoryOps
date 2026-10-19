using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;

namespace FactoryOps.Web.Controllers;

public class WorkOrdersController : Controller
{
    private readonly FactoryOpsDbContext _db;
    private readonly ILogger<WorkOrdersController> _logger;
    private readonly InventoryService _inventory;
    private readonly InventoryPlanner _planner;
    private readonly BusinessClock _clock;

    public WorkOrdersController(FactoryOpsDbContext db, ILogger<WorkOrdersController> logger, InventoryService inventory, InventoryPlanner planner, BusinessClock clock)
    {
        _db = db;
        _logger = logger;
        _inventory = inventory;
        _planner = planner;
        _clock = clock;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orders = await _db.WorkOrders.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.ConstructionSite).Include(x => x.Items)
            .OrderByDescending(x => x.Id).ToListAsync();
        ViewData["InventoryWarnings"] = (await _planner.Build()).OrderWarnings;
        return View(orders);
    }

    // Compatibility with the original single-product production form.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string productType, int quantity, DateTime dueDate)
    {
        await using var transaction = await _inventory.BeginMutation();
        var matches = await _db.Products.Where(x => x.IsActive && x.Name == productType).ToListAsync();
        if (!ModelState.IsValid || matches.Count != 1 ||
            quantity <= 0 || dueDate.Date < _clock.Today)
            return BadRequest("Invalid order details.");

        var order = new WorkOrder
        {
            Title = $"Production - {productType}", ProductType = productType,
            Quantity = quantity, DueDate = dueDate.Date, Status = "Pending",
            Items = new() { new() { ProductType = matches[0].Name, ProductId = matches[0].Id,
                ProductSkuSnapshot = matches[0].Sku, UnitOfMeasureSnapshot = matches[0].UnitOfMeasure, Quantity = quantity } }
        };
        _db.WorkOrders.Add(order);
        try
        {
            await _inventory.Reserve(order);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (InventoryException error) { return BadRequest(error.Message); }
        catch (DbUpdateException error) { _logger.LogWarning(error, "Could not confirm legacy order"); return Conflict("The order could not be confirmed. Nothing was saved."); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, uint? version)
    {
        await using var transaction = await _inventory.BeginMutation();
        if (!new[] { "Pending", "In Progress", "Completed" }.Contains(status))
            return BadRequest("Invalid status.");
        var order = await _db.WorkOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();
        if (!ModelState.IsValid || (version.HasValue && version != order.Version))
        { TempData["OrderMessage"] = "This order changed. Reload before updating its status."; return RedirectToAction(nameof(Index)); }
        order.Status = status;
        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["OrderMessage"] = "Production status updated. Actual stock changes only when the order is shipped.";
        }
        catch (InventoryException error) { TempData["OrderMessage"] = error.Message; }
        catch (DbUpdateConcurrencyException)
        {
            TempData["OrderMessage"] = "This order changed. Review its current status and try again.";
        }
        catch (DbUpdateException error)
        {
            _logger.LogWarning(error, "Could not update order status and inventory {OrderId}", id);
            TempData["OrderMessage"] = "Order status and inventory could not be saved. Nothing changed. Reload and retry.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> New()
    {
        var model = new WorkOrderForm { CreationToken = Guid.NewGuid(), Items = new() { new() } };
        await LoadChoices(model);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.WorkOrders.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.ConstructionSite).Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();
        ViewData["FulfillmentStatus"] = order.FulfillmentStatus;
        var model = new WorkOrderForm
        {
            Version = order.Version, CustomerId = order.CustomerId ?? 0,
            ConstructionSiteId = order.ConstructionSiteId, DeliveryType = order.DeliveryType,
            DueDate = order.DueDate, DeliveryInstructions = order.DeliveryInstructions,
            Items = order.Items.OrderBy(x => x.Id).Select(x => new WorkOrderItemForm
            {
                Id = x.Id, ProductId = x.ProductId, ProductType = x.ProductType, Quantity = x.Quantity
            }).ToList()
        };
        // Older orders have their only line stored in the legacy header columns.
        if (model.Items.Count == 0)
            model.Items.Add(new() { ProductType = order.ProductType, Quantity = order.Quantity });
        await LoadChoices(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(WorkOrderForm model)
    {
        await using var transaction = await _inventory.BeginMutation();
        if (model.CreationToken == Guid.Empty) ModelState.AddModelError("", "Reload the order form before confirming.");
        if (model.CreationToken.HasValue && await _db.WorkOrders.AnyAsync(x => x.CreationToken == model.CreationToken))
        { TempData["OrderMessage"] = "This order was already confirmed. No duplicate hold was added."; return RedirectToAction(nameof(Index)); }
        var address = await ValidateOrder(model);
        if (model.Items.Any(x => x.Id.HasValue))
            ModelState.AddModelError("", "New orders cannot contain existing item IDs.");
        if (!ModelState.IsValid) return await FormError("New", model);

        var order = new WorkOrder { Status = "Pending", CreationToken = model.CreationToken };
        ApplyHeader(order, model, address!);
        order.Items = model.Items.Select(x =>
        {
            var item = new WorkOrderItem();
            ApplyItem(item, x);
            return item;
        }).ToList();
        _db.WorkOrders.Add(order);
        try
        {
            await _inventory.Reserve(order);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (InventoryException error)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", error.Message);
            return await FormError("New", model);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Could not create work order");
            ModelState.AddModelError("", "The order could not be saved. Check the customer and delivery location, then try again.");
            return await FormError("New", model);
        }
        TempData["OrderMessage"] = $"Order WO-{order.Id} confirmed. Outbound stock is held for its delivery date; actual stock is unchanged.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, WorkOrderForm model)
    {
        await using var transaction = await _inventory.BeginMutation();
        var order = await _db.WorkOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();
        ViewData["FulfillmentStatus"] = order.FulfillmentStatus;

        var address = await ValidateOrder(model, order);
        if (model.Version != order.Version)
            ModelState.AddModelError("", "This order has changed since you opened it. Reload the order before saving.");
        var ids = model.Items.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count || ids.Any(itemId => !order.Items.Any(x => x.Id == itemId)))
            ModelState.AddModelError("", "An item does not belong to this order, or was submitted more than once.");
        var immutableStock = order.FulfillmentStatus is "Shipped" or "Historical";
        if (immutableStock && (model.DueDate?.Date != order.DueDate.Date || model.ConfirmCurrentUnits ||
            model.Items.Count != order.Items.Count || model.Items.Any(x => !order.Items.Any(i => i.Id == x.Id && i.ProductId == x.ProductId && i.Quantity == x.Quantity))))
            ModelState.AddModelError("", "Shipped or historical product lines and delivery dates are fixed. Delivery notes and customer details can still be updated.");
        if (!ModelState.IsValid) return await FormError("Edit", model);

        ApplyHeader(order, model, address!);
        foreach (var removed in order.Items.Where(x => !ids.Contains(x.Id)).ToList())
            _db.WorkOrderItems.Remove(removed);
        foreach (var input in model.Items)
        {
            var item = input.Id.HasValue
                ? order.Items.Single(x => x.Id == input.Id.Value)
                : new WorkOrderItem();
            ApplyItem(item, input);
            if (model.ConfirmCurrentUnits && !immutableStock) item.UnitOfMeasureSnapshot = input.SelectedProduct!.UnitOfMeasure;
            if (!input.Id.HasValue) order.Items.Add(item);
        }
        // xmin also catches changes made between loading the order and saving it.
        _db.Entry(order).Property(x => x.Version).OriginalValue = model.Version;
        // Force a header UPDATE even for item-only edits, so xmin protects the entire order.
        _db.Entry(order).Property(x => x.Title).IsModified = true;
        try
        {
            // One SaveChanges transaction covers the header, updates, inserts and deletes.
            if (order.FulfillmentStatus is "Reserved" or "Unconfirmed") await _inventory.Reserve(order);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (InventoryException error)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", error.Message);
            return await FormError("Edit", model);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "This order changed while you were saving. Reload it before trying again.");
            return await FormError("Edit", model);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Could not update work order {OrderId}", id);
            ModelState.AddModelError("", "No changes were saved. Check the customer and delivery location, then try again.");
            return await FormError("Edit", model);
        }
        TempData["OrderMessage"] = $"Order WO-{order.Id} updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Confirm(int id, uint? version) => ChangeFulfillment(id, version, "Confirm");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Ship(int id, uint? version) => ChangeFulfillment(id, version, "Ship");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, uint? version) => ChangeFulfillment(id, version, "Cancel");

    private async Task<IActionResult> ChangeFulfillment(int id, uint? version, string action)
    {
        await using var transaction = await _inventory.BeginMutation();
        var order = await _db.WorkOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();
        if (!ModelState.IsValid || version != order.Version)
        { TempData["OrderMessage"] = "This order changed or was already processed. Reload it; no extra stock was moved."; return RedirectToAction(nameof(Index)); }
        try
        {
            if (action == "Confirm") await _inventory.Reserve(order);
            else if (action == "Ship") await _inventory.Ship(order);
            else await _inventory.Cancel(order);
            _db.Entry(order).Property(x => x.Title).IsModified = true;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["OrderMessage"] = action == "Ship" ? $"WO-{id} shipped. Held quantity was released and actual stock deducted once." :
                action == "Cancel" ? $"WO-{id} cancelled. Its outbound holds were released; actual stock is unchanged." :
                $"WO-{id} confirmed. Outbound stock is held for its delivery date.";
        }
        catch (InventoryException error) { TempData["OrderMessage"] = error.Message; }
        catch (DbUpdateException error)
        {
            _logger.LogWarning(error, "Order hold/shipment failed {OrderId}", id);
            TempData["OrderMessage"] = "The order or stock changed. No partial changes were saved. Reload and retry.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> ValidateOrder(WorkOrderForm model, WorkOrder? original = null)
    {
        model.Items ??= new();
        var products = await _db.Products.AsNoTracking().ToListAsync();
        if (model.Items.Count == 0)
            ModelState.AddModelError(nameof(model.Items), "Add at least one valid order item.");
        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            var previous = original?.Items.SingleOrDefault(x => x.Id == item.Id);
            var matches = item.ProductId.HasValue
                ? products.Where(x => x.Id == item.ProductId.Value).ToList()
                : products.Where(x => x.Name == item.ProductType).ToList();
            if (matches.Count != 1 || (!matches[0].IsActive && previous?.ProductId != matches[0].Id))
                ModelState.AddModelError($"Items[{i}].ProductId", $"Item {i + 1}: select a valid product that is active. An inactive product may only be kept on its existing line.");
            else
            {
                item.SelectedProduct = matches[0];
                item.ProductId = matches[0].Id;
                item.ProductType = previous?.ProductId == item.ProductId ? previous.ProductType : matches[0].Name;
            }
            if (item.Quantity <= 0)
                ModelState.AddModelError($"Items[{i}].Quantity", $"Item {i + 1}: quantity must be positive.");
        }
        if (model.Items.Sum(x => (long)x.Quantity) > int.MaxValue)
            ModelState.AddModelError(nameof(model.Items), "The total quantity is too large.");
        if (!model.DueDate.HasValue || model.DueDate.Value == default)
            ModelState.AddModelError(nameof(model.DueDate), "Enter a valid requested delivery date.");
        else if (model.DueDate.Value.Date < _clock.Today && model.DueDate.Value.Date != original?.DueDate.Date)
            ModelState.AddModelError(nameof(model.DueDate), "A new delivery date cannot be in the past.");

        var customer = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.CustomerId);
        if (customer == null)
            ModelState.AddModelError(nameof(model.CustomerId), "Select an existing customer.");

        if (model.DeliveryType == "Company")
        {
            model.ConstructionSiteId = null;
            if (customer != null && string.IsNullOrWhiteSpace(customer.BillingAddress))
                ModelState.AddModelError(nameof(model.DeliveryType), "This customer has no company address. Choose an active construction site.");
            return customer?.BillingAddress;
        }
        if (model.DeliveryType != "Site")
        {
            ModelState.AddModelError(nameof(model.DeliveryType), "Choose Construction Site or Company Address.");
            return null;
        }
        var site = await _db.ConstructionSites.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == model.ConstructionSiteId && x.CustomerId == model.CustomerId && x.Status == "Active");
        if (site == null || string.IsNullOrWhiteSpace(site.Address))
        {
            ModelState.AddModelError(nameof(model.ConstructionSiteId), "Select an active construction site belonging to this customer with a delivery address.");
            return null;
        }
        return $"{site.Address}, {site.Suburb}, {site.State} {site.Postcode}";
    }

    private static void ApplyHeader(WorkOrder order, WorkOrderForm model, string address)
    {
        order.Title = $"Production - {model.Items.Count} item(s)";
        order.CustomerId = model.CustomerId;
        order.ConstructionSiteId = model.ConstructionSiteId;
        order.DeliveryType = model.DeliveryType;
        order.DeliveryAddressSnapshot = address;
        order.DeliveryInstructions = model.DeliveryInstructions ?? "";
        order.DueDate = model.DueDate!.Value.Date;
        order.ProductType = model.Items[0].ProductType!;
        order.Quantity = (int)model.Items.Sum(x => (long)x.Quantity);
    }

    private async Task LoadChoices(WorkOrderForm model)
    {
        // Include inactive products only for the lines already carrying them.
        var selectedIds = model.Items.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).ToList();
        model.AvailableProducts = await _db.Products.AsNoTracking().Include(x => x.Aliases)
            .Where(x => x.IsActive || selectedIds.Contains(x.Id)).OrderBy(x => x.Name).ThenBy(x => x.Sku).ToListAsync();
        foreach (var item in model.Items.Where(x => !x.ProductId.HasValue && !string.IsNullOrEmpty(x.ProductType)))
        {
            var matches = model.AvailableProducts.Where(x => x.Name == item.ProductType).ToList();
            if (matches.Count == 1) item.ProductId = matches[0].Id;
        }
        model.SelectedCustomer = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == model.CustomerId);
        model.AvailableSites = await _db.ConstructionSites.AsNoTracking()
            .Where(x => x.CustomerId == model.CustomerId && x.Status == "Active")
            .OrderBy(x => x.SiteName).ToListAsync();
    }

    private async Task<ViewResult> FormError(string view, WorkOrderForm model)
    {
        await LoadChoices(model);
        return View(view, model);
    }

    private static void ApplyItem(WorkOrderItem item, WorkOrderItemForm input)
    {
        if (item.Id == 0 || item.ProductId != input.ProductId)
        {
            item.ProductId = input.SelectedProduct!.Id;
            item.ProductType = input.SelectedProduct.Name;
            item.ProductSkuSnapshot = input.SelectedProduct.Sku;
            item.UnitOfMeasureSnapshot = input.SelectedProduct.UnitOfMeasure;
        }
        item.Quantity = input.Quantity;
    }
}
