using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

public class SettingsController(FactoryOpsDbContext db, CompanyContext company, IConfiguration configuration) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        SetConnectionStatus();
        return View(SettingsForm.From(await db.AiWorkflowSettings.AsNoTracking().SingleAsync()));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsForm model)
    {
        if (Request.Form["__CompanyKey"] != company.Key)
            return BadRequest("Reload settings for the active company before saving.");
        SetConnectionStatus();
        var settings = await db.AiWorkflowSettings.SingleAsync();
        if (model.Version != settings.Version)
            ModelState.AddModelError("", "Settings changed in another tab. Reload this page before saving; nothing was changed.");
        if (!ModelState.IsValid) return View(model);

        model.ApplyTo(settings);
        // Even an unchanged save consumes this form version, rejecting repeated submissions.
        db.Entry(settings).Property(x => x.IntakeMode).IsModified = true;
        db.Entry(settings).Property(x => x.Version).OriginalValue = model.Version;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError("", "Settings changed in another tab. Reload this page before saving; nothing was changed.");
            return View(model);
        }
        TempData["SettingsMessage"] = "Settings saved for this company. Intake and messaging connections remain pending implementation.";
        return RedirectToAction(nameof(Index));
    }

    private void SetConnectionStatus() => ViewData["OpenAiKeyConfigured"] =
        !string.IsNullOrWhiteSpace(configuration["OPENAI_API_KEY"]);
}
