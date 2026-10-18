using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace FactoryOps.Web.Controllers;

public class CustomersController : Controller
{
    private readonly FactoryOpsDbContext _db;

    public CustomersController(FactoryOpsDbContext db)
    {
        _db = db;
    }

    // GET /Customers
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.CompanyName)
            .ToListAsync();

        return View(customers);
    }
    // POST /Customers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.CompanyName))
        {
            return BadRequest("Company name is required.");
        }

        var newCustomer = new Customer
        {
            CompanyName = customer.CompanyName.Trim(),
            ContactName = customer.ContactName ?? "",
            Phone = customer.Phone ?? "",
            Email = customer.Email ?? "",
            BillingAddress = customer.BillingAddress ?? "",
            Notes = customer.Notes ?? ""
        };

        _db.Customers.Add(newCustomer);

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // POST: /Customers/AddSite
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSite(
        int customerId,
        string siteName,
        string address,
        string suburb,
        string state,
        string postcode,
        string contactName,
        string contactPhone)
    {
        // Check that the customer exists
        var customerExists = await _db.Customers
            .AnyAsync(c => c.Id == customerId);

        if (!customerExists)
            return NotFound("Customer not found.");

        if (string.IsNullOrWhiteSpace(siteName) ||
            string.IsNullOrWhiteSpace(address))
        {
            return BadRequest("Site name and address are required.");
        }

        var site = new ConstructionSite
        {
            CustomerId = customerId,
            SiteName = siteName.Trim(),
            Address = address.Trim(),
            Suburb = suburb ?? "",
            State = string.IsNullOrWhiteSpace(state) ? "NSW" : state,
            Postcode = postcode ?? "",
            ContactName = contactName ?? "",
            ContactPhone = contactPhone ?? "",
            Status = "Active"
        };

        _db.ConstructionSites.Add(site);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Please select a CSV file.");

        if (!Path.GetExtension(file.FileName)
                .Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only CSV files are supported.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("CSV file must be under 5 MB.");

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(
            reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim
            });

        var records = csv.GetRecords<CustomerSiteCsvRow>().ToList();

        if (records.Count > 1000)
            return BadRequest("Maximum 1000 rows per import.");

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.CompanyName))
                continue;

            var companyName = record.CompanyName.Trim();

            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.CompanyName == companyName);

            if (customer == null)
            {
                customer = new Customer
                {
                    CompanyName = companyName,
                    ContactName = record.ContactName ?? "",
                    Phone = record.Phone ?? "",
                    Email = record.Email ?? "",
                    BillingAddress = record.BillingAddress ?? "",
                    Notes = ""
                };

                _db.Customers.Add(customer);
                await _db.SaveChangesAsync();
            }

            if (string.IsNullOrWhiteSpace(record.SiteName))
                continue;

            var siteName = record.SiteName.Trim();

            bool siteExists = await _db.ConstructionSites
                .AnyAsync(s =>
                    s.CustomerId == customer.Id &&
                    s.SiteName == siteName);

            if (!siteExists)
            {
                _db.ConstructionSites.Add(new ConstructionSite
                {
                    CustomerId = customer.Id,
                    SiteName = siteName,
                    Address = record.Address ?? "",
                    Suburb = record.Suburb ?? "",
                    State = record.State ?? "NSW",
                    Postcode = record.Postcode ?? "",
                    ContactName = record.SiteContactName ?? "",
                    ContactPhone = record.SiteContactPhone ?? "",
                    Status = "Active"
                });

                await _db.SaveChangesAsync();
            }
        }

        return RedirectToAction(nameof(Index));
    }


    // GET /Customers/Sites?customerId=1
    [HttpGet]
    public async Task<IActionResult> Sites(int customerId)
    {
        var customerExists = await _db.Customers
            .AnyAsync(c => c.Id == customerId);

        if (!customerExists)
            return NotFound();

        var sites = await _db.ConstructionSites
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .OrderBy(s => s.SiteName)
            .Select(s => new
            {
                s.Id,
                s.SiteName,
                s.Address,
                s.Suburb,
                s.State,
                s.Postcode,
                s.ContactName,
                s.ContactPhone,
                s.Status
            })
            .ToListAsync();

        return Json(sites);
    }

}