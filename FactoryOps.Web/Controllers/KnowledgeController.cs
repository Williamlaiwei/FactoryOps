using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FactoryOps.Web.Controllers;

public class KnowledgeController(KnowledgeClient knowledge) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var model = new KnowledgePage { Query = q?.Trim() ?? "" };
        try
        {
            model.Documents = (await knowledge.Json<KnowledgeDocumentList>(HttpMethod.Get, "v1/documents")).Documents;
            if (model.Query.Length > 1000) model.Error = "Search text must be 1,000 characters or fewer.";
            else if (model.Query.Length > 0)
                model.Result = await knowledge.Json<KnowledgeSearchResult>(HttpMethod.Post, "v1/retrieve", JsonContent.Create(new { query = model.Query }));
        }
        catch (KnowledgeException error) { model.Error = error.Message; }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(22 * 1024 * 1024)]
    public Task<IActionResult> Upload(IFormFile? file, string? sourceUrl) => SaveFile(file, sourceUrl);

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(22 * 1024 * 1024)]
    public Task<IActionResult> Replace(Guid id, int version, IFormFile? file, string? sourceUrl) => SaveFile(file, sourceUrl, id, version);

    private async Task<IActionResult> SaveFile(IFormFile? file, string? sourceUrl, Guid? id = null, int? version = null)
    {
        if (!ModelState.IsValid || file == null || file.Length == 0 || file.Length > 20 * 1024 * 1024 ||
            !new[] { ".pdf", ".csv", ".xlsx" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            TempData["KnowledgeError"] = "Choose a nonempty PDF, CSV or XLSX document no larger than 20 MB.";
        else
        {
            try
            {
                var document = await knowledge.Upload(file, sourceUrl, id, version);
                TempData["KnowledgeMessage"] = document.Duplicate ? "This document is already indexed for this company." : "Document saved and indexed. Products and orders were not changed.";
            }
            catch (KnowledgeException error) { TempData["KnowledgeError"] = error.Message; }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex(Guid id, int version)
    {
        try
        {
            await knowledge.Json<KnowledgeDocument>(HttpMethod.Post, $"v1/documents/{id}/reindex", JsonContent.Create(new { version }));
            TempData["KnowledgeMessage"] = "Document re-indexed.";
        }
        catch (KnowledgeException error) { TempData["KnowledgeError"] = error.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, int version)
    {
        try
        {
            using var response = await knowledge.Send(HttpMethod.Delete, $"v1/documents/{id}?version={version}");
            TempData["KnowledgeMessage"] = "Document, stored original and index entries deleted.";
        }
        catch (KnowledgeException error) { TempData["KnowledgeError"] = error.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Original(Guid id)
    {
        try
        {
            using var response = await knowledge.Send(HttpMethod.Get, $"v1/documents/{id}/original");
            return File(await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", $"knowledge-{id}");
        }
        catch (KnowledgeException error) { return StatusCode((int)error.Status, error.Message); }
    }

    // Stable JSON boundary for the later order-extraction/review workflow.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Retrieve(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1000) return BadRequest("Enter search text up to 1,000 characters.");
        try
        {
            return Json(await knowledge.Json<KnowledgeSearchResult>(HttpMethod.Post, "v1/retrieve", JsonContent.Create(new { query })));
        }
        catch (KnowledgeException error) { return StatusCode((int)error.Status, new { error = error.Message }); }
    }
}
