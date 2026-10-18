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
}