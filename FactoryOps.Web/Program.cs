using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<CompanyFormFilter>());
builder.Services.AddScoped<CompanyContext>();
builder.Services.AddScoped<ProductCatalog>();
builder.Services.AddScoped<InventoryPlanner>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BusinessClock>();
builder.Services.AddHttpClient<KnowledgeClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["KnowledgeService:BaseUrl"] ?? "http://ai:8000/");
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddDbContext<FactoryOpsDbContext>(
    options => options.UseNpgsql(
        builder.Configuration.GetConnectionString("FactoryOps")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.Use(async (context, next) =>
{
    var company = context.RequestServices.GetRequiredService<CompanyContext>();
    var db = context.RequestServices.GetRequiredService<FactoryOpsDbContext>();
    var key = context.Request.Cookies["FactoryOps.Company"] ?? "fti-demo";
    var internalRequest = context.Request.Path.StartsWithSegments("/internal/catalog");
    if (internalRequest)
    {
        var configured = builder.Configuration["KnowledgeService:ApiKey"] ?? "";
        var supplied = context.Request.Headers["X-FactoryOps-Key"].ToString();
        if (configured.Length < 32 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(configured), System.Text.Encoding.UTF8.GetBytes(supplied)))
        { context.Response.StatusCode = 401; return; }
        key = context.Request.Headers["X-FactoryOps-Company"].ToString();
    }
    var profile = await db.CompanyProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key);
    if (profile == null && internalRequest) { context.Response.StatusCode = 404; return; }
    company.Profile = profile ?? await db.CompanyProfiles.AsNoTracking().SingleAsync(x => x.Key == "fti-demo");
    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<FactoryOpsDbContext>();

    db.Database.EnsureCreated();
    // EnsureCreated does not upgrade an existing database. Apply the additive,
    // idempotent catalog SQL to both existing and freshly created databases.
    using var transaction = db.Database.BeginTransaction();
    foreach (var sqlFile in Directory.GetFiles(Path.Combine(app.Environment.ContentRootPath, "Data", "Sql"), "*.sql").OrderBy(x => x))
        db.Database.ExecuteSqlRaw(File.ReadAllText(sqlFile));
    transaction.Commit();
}
app.Run();
