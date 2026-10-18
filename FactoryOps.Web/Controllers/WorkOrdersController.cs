using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;

namespace FactoryOps.Web.Controllers;

public class WorkOrdersController : Controller
{
    private readonly FactoryOpsDbContext _db;
    private readonly ILogger<WorkOrdersController> _logger;

    public WorkOrdersController(FactoryOpsDbContext db, ILogger<WorkOrdersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orders = await _db.WorkOrders.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.ConstructionSite).Include(x => x.Items)
            .OrderByDescending(x => x.Id).ToListAsync();
        return View(orders);
    }

    // Compatibility with the original single-product production form.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string productType, int quantity, DateTime dueDate)
    {
        if (!ModelState.IsValid || !OrderProducts.Allowed.Contains(productType) ||
            quantity <= 0 || dueDate.Date < DateTime.Today)
            return BadRequest("Invalid order details.");

        _db.WorkOrders.Add(new WorkOrder
        {
            Title = $"Production - {productType}", ProductType = productType,
            Quantity = quantity, DueDate = dueDate.Date, Status = "Pending",
            Items = new() { new() { ProductType = productType, Quantity = quantity } }
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (!new[] { "Pending", "In Progress", "Completed" }.Contains(status))
            return BadRequest("Invalid status.");
        var order = await _db.WorkOrders.FindAsync(id);
        if (order == null) return NotFound();
        order.Status = status;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["OrderMessage"] = "This order changed. Review its current status and try again.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult New() => View(new WorkOrderForm
    {
        Items = new() { new() }
    });

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.WorkOrders.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.ConstructionSite).Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();
        var model = new WorkOrderForm
        {
            Version = order.Version, CustomerId = order.CustomerId ?? 0,
            ConstructionSiteId = order.ConstructionSiteId, DeliveryType = order.DeliveryType,
            DueDate = order.DueDate, DeliveryInstructions = order.DeliveryInstructions,
            Items = order.Items.OrderBy(x => x.Id).Select(x => new WorkOrderItemForm
            {
                Id = x.Id, ProductType = x.ProductType, Quantity = x.Quantity
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
        var address = await ValidateOrder(model);
        if (model.Items.Any(x => x.Id.HasValue))
            ModelState.AddModelError("", "New orders cannot contain existing item IDs.");
        if (!ModelState.IsValid) return await FormError("New", model);

        var order = new WorkOrder { Status = "Pending" };
        ApplyHeader(order, model, address!);
        order.Items = model.Items.Select(x => new WorkOrderItem
        {
            ProductType = x.ProductType, Quantity = x.Quantity
        }).ToList();
        _db.WorkOrders.Add(order);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Could not create work order");
            ModelState.AddModelError("", "The order could not be saved. Check the customer and delivery location, then try again.");
            return await FormError("New", model);
        }
        TempData["OrderMessage"] = $"Order WO-{order.Id} created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, WorkOrderForm model)
    {
        var order = await _db.WorkOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
        if (order == null) return NotFound();

        var address = await ValidateOrder(model, order.DueDate);
        if (model.Version != order.Version)
            ModelState.AddModelError("", "This order has changed since you opened it. Reload the order before saving.");
        var ids = model.Items.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count || ids.Any(itemId => !order.Items.Any(x => x.Id == itemId)))
            ModelState.AddModelError("", "An item does not belong to this order, or was submitted more than once.");
        if (!ModelState.IsValid) return await FormError("Edit", model);

        ApplyHeader(order, model, address!);
        foreach (var removed in order.Items.Where(x => !ids.Contains(x.Id)).ToList())
            _db.WorkOrderItems.Remove(removed);
        foreach (var input in model.Items)
        {
            var item = input.Id.HasValue
                ? order.Items.Single(x => x.Id == input.Id.Value)
                : new WorkOrderItem();
            item.ProductType = input.ProductType;
            item.Quantity = input.Quantity;
            if (!input.Id.HasValue) order.Items.Add(item);
        }
        // xmin also catches changes made between loading the order and saving it.
        _db.Entry(order).Property(x => x.Version).OriginalValue = model.Version;
        // Force a header UPDATE even for item-only edits, so xmin protects the entire order.
        _db.Entry(order).Property(x => x.Title).IsModified = true;
        try
        {
            // One SaveChanges transaction covers the header, updates, inserts and deletes.
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError("", "This order changed while you were saving. Reload it before trying again.");
            return await FormError("Edit", model);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Could not update work order {OrderId}", id);
            ModelState.AddModelError("", "No changes were saved. Check the customer and delivery location, then try again.");
            return await FormError("Edit", model);
        }
        TempData["OrderMessage"] = $"Order WO-{order.Id} updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> ValidateOrder(WorkOrderForm model, DateTime? originalDate = null)
    {
        model.Items ??= new();
        if (model.Items.Count == 0)
            ModelState.AddModelError(nameof(model.Items), "Add at least one valid order item.");
        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            if (!OrderProducts.Allowed.Contains(item.ProductType))
                ModelState.AddModelError($"Items[{i}].ProductType", $"Item {i + 1}: select a valid product.");
            if (item.Quantity <= 0)
                ModelState.AddModelError($"Items[{i}].Quantity", $"Item {i + 1}: quantity must be positive.");
        }
        if (model.Items.Sum(x => (long)x.Quantity) > int.MaxValue)
            ModelState.AddModelError(nameof(model.Items), "The total quantity is too large.");
        if (!model.DueDate.HasValue || model.DueDate.Value == default)
            ModelState.AddModelError(nameof(model.DueDate), "Enter a valid requested delivery date.");
        else if (model.DueDate.Value.Date < DateTime.Today && model.DueDate.Value.Date != originalDate?.Date)
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
        order.ProductType = model.Items[0].ProductType;
        order.Quantity = (int)model.Items.Sum(x => (long)x.Quantity);
    }

    private async Task LoadChoices(WorkOrderForm model)
    {
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
}
