using FactoryOps.Web.Data;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

// Authenticated by the internal-service middleware. Read-only; no order endpoints.
[Route("internal/catalog")]
public class InternalCatalogController(FactoryOpsDbContext db, CompanyContext company) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var products = await db.Products.AsNoTracking().Include(x => x.Aliases).Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync();
        return Json(new
        {
            companyKey = company.Key,
            products = products.Select(x => new
            {
                x.Id, x.Sku, x.Name, x.UnitOfMeasure, x.IsDemo, x.Dimensions, x.PackingSize, x.RoughIn,
                x.HeatingMethod, x.FlushingMethod, x.CeramicBody, x.Color, x.DisplayMode, x.NetWeight, x.GrossWeight,
                x.Category, x.Description, x.Specifications, x.Packaging,
                skuIsVerified = x.SkuIsVerified && !x.Sku.StartsWith("LEGACY-"),
                x.SourceUrl, aliases = x.Aliases.Select(a => a.Value).ToList()
            })
        });
    }
}
