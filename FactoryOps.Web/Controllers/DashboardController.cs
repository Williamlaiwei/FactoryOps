using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DashboardController(FactoryOpsDbContext db, InventoryPlanner planner, CompanyContext company) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? productId, int months = 1)
    {
        if (!ModelState.IsValid || months is not (1 or 3)) return BadRequest("Choose a one-month or three-month range.");
        var plan = await planner.Build();
        var product = productId.HasValue ? plan.Products.SingleOrDefault(x => x.Product.Id == productId) :
            plan.Products.OrderByDescending(x => x.Stock != null).ThenBy(x => x.Product.Sku).FirstOrDefault();
        if (productId.HasValue && product == null) return NotFound();
        return View(new InventoryDashboard
        {
            Products = plan.Products.Select(x => x.Product).ToList(), ProductPlans = plan.Products, SelectedProductId = product?.Product.Id ?? 0,
            Months = months, Projection = product == null ? null : InventoryPlanner.Project(plan, product, months, company.Key),
            RiskProducts = plan.Products.Count(x => x.HasDashboardRisk),
            UnconfirmedOrders = await db.WorkOrders.CountAsync(x => x.FulfillmentStatus == "Unconfirmed")
        });
    }

    [HttpGet]
    public async Task<IActionResult> Projection(int productId, int months = 1)
    {
        if (!ModelState.IsValid || months is not (1 or 3)) return BadRequest("Choose a one-month or three-month range.");
        var plan = await planner.Build();
        var product = plan.Products.SingleOrDefault(x => x.Product.Id == productId);
        if (product == null) return NotFound();
        return Json(InventoryPlanner.Project(plan, product, months, company.Key));
    }
}
