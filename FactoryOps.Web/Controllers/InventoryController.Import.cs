using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Controllers;

public partial class InventoryController
{
    private IDataProtector StockImportProtector => protection.CreateProtector("FactoryOps.Inventory.CsvPreview.v1");
    private IDataProtector StockExportProtector => protection.CreateProtector("FactoryOps.Inventory.CsvExport.v1");

    [HttpGet]
    public IActionResult ImportCsv() => View(new StockImportForm());

    [HttpGet]
    public IActionResult StockCsvTemplate() => File(CsvImportExamples.Download(CsvImportExamples.Stock),
        "text/csv; charset=utf-8", "stock-template.csv");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportStockCsv()
    {
        // A single query captures catalog and stock versions from the same snapshot.
        var stocks = await db.InventoryStocks.AsNoTracking().Include(x => x.Product)
            .OrderBy(x => x.Product.Sku).ThenBy(x => x.ProductId).ToListAsync();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using (var csv = new CsvWriter(output, CultureInfo.InvariantCulture))
        {
            foreach (var heading in new[] { "Sku", "UnitOfMeasure", "CountedQuantity", "ReorderPoint", "LeadTimeDays", "Reason", "StockSnapshot" }) csv.WriteField(heading);
            csv.NextRecord();
            foreach (var stock in stocks)
            {
                var snapshot = new StockExportSnapshot { CompanyKey = company.Key, ProductId = stock.ProductId,
                    ProductVersion = stock.Product.Version, StockVersion = stock.Version };
                csv.WriteField(stock.Product.Sku); csv.WriteField(stock.Product.UnitOfMeasure);
                csv.WriteField(stock.OnHand); csv.WriteField(stock.ReorderPoint); csv.WriteField(stock.LeadTimeDays);
                csv.WriteField(""); csv.WriteField(StockExportProtector.Protect(JsonSerializer.Serialize(snapshot)));
                csv.NextRecord();
            }
        }
        logger.LogInformation("Stock CSV exported for company {CompanyKey}: {ProductCount} tracked products", company.Key, stocks.Count);
        return File(CsvImportExamples.Download(output.ToString()), "text/csv; charset=utf-8",
            $"stock-{company.Key}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ImportCsv(StockImportForm model)
    {
        if (model.File != null && (model.File.Length == 0 || model.File.Length > 5 * 1024 * 1024 ||
            !Path.GetExtension(model.File.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(model.File), "Choose a nonempty CSV file no larger than 5 MB.");
        if (!ModelState.IsValid) return View(model);
        var rows = new List<StockImportRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(model.File!.OpenReadStream(), new UTF8Encoding(false, true), true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim, DetectColumnCountChanges = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
        });
        try
        {
            if (!await csv.ReadAsync()) ModelState.AddModelError("", "The CSV is empty.");
            else
            {
                csv.ReadHeader();
                var headers = (csv.HeaderRecord ?? Array.Empty<string>()).Select(x => x.Trim()).ToArray();
                var columns = new[] { "Sku", "UnitOfMeasure", "CountedQuantity", "ReorderPoint", "LeadTimeDays", "Reason", "StockSnapshot" };
                if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                    ModelState.AddModelError("", "CSV column names must be unique.");
                foreach (var required in columns.Take(3))
                    if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                        ModelState.AddModelError("", $"Missing required CSV column: {required}.");
                foreach (var unknown in headers.Where(x => !columns.Contains(x, StringComparer.OrdinalIgnoreCase)))
                    ModelState.AddModelError("", $"Unknown CSV column: {unknown}. Use the stock CSV example headings.");
                if (!ModelState.IsValid) return View(model);
                string Field(string name) => csv.TryGetField<string>(name, out var value) ? value?.Trim() ?? "" : "";
                while (await csv.ReadAsync())
                {
                    if (rows.Count >= 1000) { ModelState.AddModelError("", "Maximum 1000 stock rows per import."); break; }
                    var row = new StockImportRow { RowNumber = csv.Parser.Row, Sku = Field("Sku").ToUpperInvariant(),
                        UnitOfMeasure = Field("UnitOfMeasure"), Reason = Field("Reason"), SourceSnapshot = Field("StockSnapshot") };
                    if (headers.Contains("StockSnapshot", StringComparer.OrdinalIgnoreCase) && row.SourceSnapshot.Length == 0)
                        ModelState.AddModelError("", $"Row {row.RowNumber}: keep StockSnapshot unchanged when editing a downloaded file.");
                    long? Number(string name, long maximum)
                    {
                        var value = Field(name);
                        if (value.Length == 0) return null;
                        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number <= maximum) return number;
                        ModelState.AddModelError("", $"Row {row.RowNumber}: {name} must be a whole number from 0 to {maximum}.");
                        return null;
                    }
                    row.CountedQuantity = Number("CountedQuantity", StockSettingsForm.MaximumQuantity);
                    row.ReorderPoint = Number("ReorderPoint", StockSettingsForm.MaximumQuantity);
                    row.LeadTimeDays = (int?)Number("LeadTimeDays", 3650);
                    if (row.Sku.Length == 0 || row.Sku.Length > 64) ModelState.AddModelError("", $"Row {row.RowNumber}: enter an existing product SKU (maximum 64 characters).");
                    if (row.UnitOfMeasure.Length == 0 || row.UnitOfMeasure.Length > 32) ModelState.AddModelError("", $"Row {row.RowNumber}: enter the product's catalog UnitOfMeasure.");
                    if (row.Reason.Length > 500) ModelState.AddModelError("", $"Row {row.RowNumber}: Reason must be 500 characters or fewer.");
                    if (!seen.Add(row.Sku)) ModelState.AddModelError("", $"Row {row.RowNumber}: duplicate SKU within this file.");
                    if (row.CountedQuantity == null && row.ReorderPoint == null && row.LeadTimeDays == null)
                        ModelState.AddModelError("", $"Row {row.RowNumber}: enter a count or at least one stock setting.");
                    rows.Add(row);
                    if (ModelState.ErrorCount >= 50) { ModelState.AddModelError("", "Too many errors. Correct these rows and retry."); break; }
                }
            }
        }
        catch (Exception error) when (error is CsvHelperException or DecoderFallbackException)
        {
            ModelState.AddModelError("", $"CSV row {csv.Parser.Row} could not be read. Use UTF-8 CSV with consistent columns and correctly quoted values.");
        }
        if (rows.Count == 0) ModelState.AddModelError("", "The CSV must contain at least one stock row.");
        if (!ModelState.IsValid) return View(model);

        // Match within the active company and capture a consistent stock snapshot.
        await using var transaction = await inventory.BeginMutation();
        var skus = rows.Select(x => x.Sku).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => skus.Contains(x.Sku)).ToDictionaryAsync(x => x.Sku);
        var ids = products.Values.Select(x => x.Id).ToArray();
        var stocks = await db.InventoryStocks.AsNoTracking().Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId);
        foreach (var row in rows)
        {
            if (!products.TryGetValue(row.Sku, out var product))
            { ModelState.AddModelError("", $"Row {row.RowNumber}: SKU {row.Sku} was not found in the active company. Add it to the catalog first."); continue; }
            stocks.TryGetValue(product.Id, out var stock);
            if (row.SourceSnapshot.Length > 0)
            {
                StockExportSnapshot? snapshot = null;
                try { snapshot = JsonSerializer.Deserialize<StockExportSnapshot>(StockExportProtector.Unprotect(row.SourceSnapshot)); }
                catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { }
                if (snapshot == null || snapshot.CompanyKey != company.Key || snapshot.ProductId != product.Id ||
                    snapshot.ProductVersion != product.Version || snapshot.StockVersion != stock?.Version)
                    ModelState.AddModelError("", $"Row {row.RowNumber}: {row.Sku} changed since download, or its StockSnapshot is invalid. Download current stock again; nothing was changed.");
                row.SourceSnapshot = "";
            }
            if (!InventoryPlanner.CompatibleUnit(row.UnitOfMeasure, product.UnitOfMeasure))
                ModelState.AddModelError("", $"Row {row.RowNumber}: {row.Sku} uses {product.UnitOfMeasure}. Quantities cannot be converted automatically.");
            if (stock == null && !row.CountedQuantity.HasValue)
                ModelState.AddModelError("", $"Row {row.RowNumber}: {row.Sku} needs an opening physical count, including zero if counted empty.");
            if (stock != null && row.CountedQuantity.HasValue && row.CountedQuantity != stock.OnHand && string.IsNullOrWhiteSpace(row.Reason))
                ModelState.AddModelError("", $"Row {row.RowNumber}: {row.Sku} needs a Reason for changing existing stock.");
            row.ProductId = product.Id; row.ProductVersion = product.Version; row.StockVersion = stock?.Version;
            row.Name = product.Name; row.UnitOfMeasure = product.UnitOfMeasure; row.PreviousOnHand = stock?.OnHand;
            row.ResolvedReorderPoint = row.ReorderPoint ?? stock?.ReorderPoint ?? 0;
            row.ResolvedLeadTimeDays = row.LeadTimeDays ?? stock?.LeadTimeDays ?? 0;
        }
        if (!ModelState.IsValid) return View(model);
        var batch = new StockImportBatch { CompanyKey = company.Key, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), Rows = rows };
        return View("ImportCsvPreview", new StockImportPreview { Rows = rows, Token = StockImportProtector.Protect(JsonSerializer.Serialize(batch)) });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ConfirmStockImport(string? token)
    {
        StockImportBatch? batch = null;
        try { if (!string.IsNullOrEmpty(token)) batch = JsonSerializer.Deserialize<StockImportBatch>(StockImportProtector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { }
        if (batch == null || batch.CompanyKey != company.Key || batch.ExpiresAt <= DateTimeOffset.UtcNow || batch.Rows.Count is < 1 or > 1000)
        {
            ModelState.AddModelError("", "This stock preview is invalid, expired or belongs to another company. Upload the CSV again; nothing was changed.");
            return View("ImportCsv", new StockImportForm());
        }
        await using var transaction = await inventory.BeginMutation();
        var ids = batch.Rows.Select(x => x.ProductId).ToArray();
        var products = await db.Products.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var stocks = await db.InventoryStocks.Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId);
        foreach (var row in batch.Rows)
        {
            products.TryGetValue(row.ProductId, out var product); stocks.TryGetValue(row.ProductId, out var stock);
            if (product == null || product.Version != row.ProductVersion || stock?.Version != row.StockVersion)
                ModelState.AddModelError("", $"{row.Sku}: product or stock changed after the preview, or this import was already saved. Upload again to review current balances; nothing was changed.");
        }
        if (!ModelState.IsValid) return View("ImportCsv", new StockImportForm());
        foreach (var row in batch.Rows)
        {
            stocks.TryGetValue(row.ProductId, out var stock);
            ApplyStockCount(products[row.ProductId], stock, row.CountedQuantity, row.ResolvedReorderPoint, row.ResolvedLeadTimeDays, row.Reason);
        }
        if (!await Save()) return View("ImportCsv", new StockImportForm());
        await transaction.CommitAsync();
        logger.LogInformation("Stock CSV imported for company {CompanyKey}: {ProductCount} products", company.Key, batch.Rows.Count);
        TempData["InventoryMessage"] = $"Imported stock counts and settings for {batch.Rows.Count} product(s). Existing order holds are unchanged. Review any shortage warnings.";
        return RedirectToAction(nameof(Index));
    }
}
