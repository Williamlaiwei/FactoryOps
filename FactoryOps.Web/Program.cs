using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Data;
using FactoryOps.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

// Lightweight Docker health probe without curl or extra runtime packages.
if (args.Contains("--healthcheck"))
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try { return (await probe.GetAsync("http://localhost:8080/healthz")).IsSuccessStatusCode ? 0 : 1; }
    catch { return 1; }
}

var builder = WebApplication.CreateBuilder(args);
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("FactoryOps");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (IPAddress.TryParse(builder.Configuration["ReverseProxy:Address"], out var proxy)) options.KnownProxies.Add(proxy);
});

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
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Container probes and the private knowledge callback use internal HTTP.
app.UseWhen(context => builder.Configuration.GetValue("ReverseProxy:RequireHttps", true) &&
    !context.Request.Path.StartsWithSegments("/internal/catalog") && context.Request.Path != "/healthz",
    branch => branch.UseHttpsRedirection());
app.UseStaticFiles();

app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/healthz") { await next(); return; }
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
app.MapGet("/healthz", async (FactoryOpsDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));

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
return 0;
