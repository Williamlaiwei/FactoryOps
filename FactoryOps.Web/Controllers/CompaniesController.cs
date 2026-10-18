using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

public class CompaniesController(FactoryOpsDbContext db, CompanyContext company, ILogger<CompaniesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await db.CompanyProfiles.AsNoTracking().OrderBy(x => x.Key).ToListAsync());

    [HttpGet]
    public IActionResult Create() => View(new CompanyCreateForm { CreationToken = Guid.NewGuid() });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyCreateForm model)
    {
        if (!ModelState.IsValid) return View(model);
        // A per-form token makes retries use the same key instead of creating another company.
        var key = "company-" + model.CreationToken!.Value.ToString("N");
        if (await db.CompanyProfiles.AnyAsync(x => x.Key == key))
        {
            SelectCompany(key);
            TempData["CompanyMessage"] = "This company was already created. No duplicate was added.";
            return RedirectToAction(nameof(Index));
        }

        var profile = new CompanyProfile { Key = key, Name = model.Name.Trim(), Website = model.Website?.Trim() ?? "", IsDemo = false };
        var previousProfile = company.Profile;
        try
        {
            // Stamp the new settings with their new company key, using the existing company context.
            company.Profile = profile;
            db.CompanyProfiles.Add(profile);
            db.AiWorkflowSettings.Add(new AiWorkflowSettings());
            // One transaction saves both records. No catalog or business data is copied.
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error)
        {
            db.ChangeTracker.Clear();
            if (await db.CompanyProfiles.AnyAsync(x => x.Key == key))
            {
                SelectCompany(key);
                TempData["CompanyMessage"] = "This company was already created. No duplicate was added.";
                return RedirectToAction(nameof(Index));
            }
            logger.LogWarning(error, "Could not create company {CompanyKey}", key);
            ModelState.AddModelError("", "The company could not be created. No company or settings were saved. Please try again.");
            return View(model);
        }
        finally { company.Profile = previousProfile; }

        SelectCompany(key);
        TempData["CompanyMessage"] = $"Company {profile.Name} created and selected. Add your products, customers and knowledge documents to get started.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string key)
    {
        if (!await db.CompanyProfiles.AnyAsync(x => x.Key == key)) return NotFound();
        SelectCompany(key);
        return RedirectToAction(nameof(Index));
    }

    private void SelectCompany(string key)
    {
        Response.Cookies.Append("FactoryOps.Company", key, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, IsEssential = true
        });
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
