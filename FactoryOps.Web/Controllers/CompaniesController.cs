using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

public class CompaniesController(FactoryOpsDbContext db, CompanyContext company) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await db.CompanyProfiles.AsNoTracking().OrderBy(x => x.Key).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string key)
    {
        if (!await db.CompanyProfiles.AnyAsync(x => x.Key == key)) return NotFound();
        Response.Cookies.Append("FactoryOps.Company", key, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, IsEssential = true
        });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string name, string? website)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || (website?.Length ?? 0) > 500 ||
            (!string.IsNullOrEmpty(website) && (!Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.Scheme != "https")))
            TempData["CompanyMessage"] = "Enter a company name and an optional valid HTTPS website.";
        else
        {
            var profile = await db.CompanyProfiles.SingleAsync(x => x.Key == company.Key);
            profile.Name = name.Trim(); profile.Website = website?.Trim() ?? "";
            await db.SaveChangesAsync();
            TempData["CompanyMessage"] = "Company settings saved.";
        }
        return RedirectToAction(nameof(Index));
    }
}
