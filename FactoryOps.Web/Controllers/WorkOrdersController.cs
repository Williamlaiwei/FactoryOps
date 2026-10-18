using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;

namespace FactoryOps.Web.Controllers;

public class WorkOrdersController : Controller
{
    private readonly FactoryOpsDbContext _db;

    public WorkOrdersController(FactoryOpsDbContext db)
    {
        _db = db;
    }

    // GET: /WorkOrders
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orders = await _db.WorkOrders
            .OrderBy(x => x.Id)
            .ToListAsync();

        return View(orders);
    }

    // POST: /WorkOrders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string productType,
        int quantity,
        DateTime dueDate)
    {
        string[] products =
        {
            "Fast Tread", "BlueDeck", "SureFit"
        };

        if (!products.Contains(productType) ||
            quantity <= 0 ||
            dueDate.Date < DateTime.Today)
        {
            return BadRequest("Invalid order details.");
        }

        var order = new WorkOrder
        {
            Title = $"Production - {productType}",
            ProductType = productType,
            Quantity = quantity,
            DueDate = dueDate,
            Status = "Pending"
        };

        _db.WorkOrders.Add(order);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // POST: /WorkOrders/UpdateStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(
        int id,
        string status)
    {
        string[] allowed =
        {
            "Pending", "In Progress", "Completed"
        };

        if (!allowed.Contains(status))
            return BadRequest("Invalid status.");

        var order = await _db.WorkOrders.FindAsync(id);

        if (order == null)
            return NotFound();

        order.Status = status;

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
    // GET /WorkOrders/New
    [HttpGet]
    public IActionResult New()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(
        int customerId,
        int? constructionSiteId,
        string deliveryType,
        string productType,
        int quantity,
        DateTime dueDate,
        string? deliveryInstructions)
    {
        // 1. Validate customer
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId);

        if (customer == null)
            return BadRequest("Customer not found.");

        // 2. Validate order details
        if (string.IsNullOrWhiteSpace(productType) ||
            quantity <= 0 ||
            dueDate.Date < DateTime.Today)
        {
            return BadRequest("Invalid order details.");
        }

        // 3. Resolve delivery address
        string deliveryAddress;

        if (deliveryType == "Site")
        {
            if (!constructionSiteId.HasValue)
                return BadRequest("Construction site is required.");

            var site = await _db.ConstructionSites
                .FirstOrDefaultAsync(s =>
                    s.Id == constructionSiteId.Value &&
                    s.CustomerId == customerId &&
                    s.Status == "Active");

            if (site == null)
                return BadRequest("Invalid or inactive construction site.");

            deliveryAddress =
                $"{site.Address}, {site.Suburb}, " +
                $"{site.State} {site.Postcode}";
        }
        else if (deliveryType == "Company")
        {
            if (string.IsNullOrWhiteSpace(customer.BillingAddress))
                return BadRequest("Company address is missing.");

            deliveryAddress = customer.BillingAddress;
            constructionSiteId = null;
        }
        else
        {
            return BadRequest("Invalid delivery type.");
        }

        // 4. Create work order
        var order = new WorkOrder
        {
            Title = $"Production - {productType}",
            CustomerId = customerId,
            ConstructionSiteId = constructionSiteId,
            ProductType = productType,
            Quantity = quantity,
            DueDate = dueDate.Date,
            Status = "Pending",
            DeliveryType = deliveryType,
            DeliveryAddressSnapshot = deliveryAddress,
            DeliveryInstructions = deliveryInstructions ?? ""
        };

        // 5. Save to PostgreSQL
        _db.WorkOrders.Add(order);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

}