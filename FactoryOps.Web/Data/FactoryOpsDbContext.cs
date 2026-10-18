using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Models;
using FactoryOps.Web.Services;

namespace FactoryOps.Web.Data;

public class FactoryOpsDbContext : DbContext
{
    private readonly CompanyContext _company;
    public FactoryOpsDbContext(
        DbContextOptions<FactoryOpsDbContext> options, CompanyContext company)
        : base(options)
    {
        _company = company;
    }

    public DbSet<WorkOrder> WorkOrders
        => Set<WorkOrder>();
    public DbSet<Customer> Customers
    => Set<Customer>();

    public DbSet<ConstructionSite> ConstructionSites
        => Set<ConstructionSite>();
    public DbSet<WorkOrderItem> WorkOrderItems => Set<WorkOrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<AiWorkflowSettings> AiWorkflowSettings => Set<AiWorkflowSettings>();
    protected override void OnModelCreating(
    ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrder>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<Product>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<AiWorkflowSettings>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<AiWorkflowSettings>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<AiWorkflowSettings>().HasOne<CompanyProfile>().WithOne()
            .HasForeignKey<AiWorkflowSettings>(x => x.CompanyKey).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>().HasIndex(x => new { x.CompanyKey, x.Sku }).IsUnique();
        modelBuilder.Entity<Product>().Property(x => x.SourceUrl).HasDefaultValue("");
        modelBuilder.Entity<Product>().Property(x => x.IsDemo).HasDefaultValue(false);
        modelBuilder.Entity<Product>().Property(x => x.SkuIsVerified).HasDefaultValue(true);
        foreach (var type in new[] { typeof(Product), typeof(Customer), typeof(ConstructionSite), typeof(WorkOrder) })
        {
            modelBuilder.Entity(type).Property(nameof(ICompanyRecord.CompanyKey)).HasMaxLength(64).HasDefaultValue("fti-demo");
            modelBuilder.Entity(type).HasOne(typeof(CompanyProfile)).WithMany().HasForeignKey(nameof(ICompanyRecord.CompanyKey)).OnDelete(DeleteBehavior.Restrict);
        }
        modelBuilder.Entity<Product>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<Customer>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<ConstructionSite>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<WorkOrder>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<WorkOrderItem>().HasQueryFilter(x => x.WorkOrder.CompanyKey == _company.Key);
        modelBuilder.Entity<ProductAlias>().HasQueryFilter(x => x.Product.CompanyKey == _company.Key);
        modelBuilder.Entity<ProductAlias>().HasIndex(x => new { x.ProductId, x.NormalizedValue }).IsUnique();
        modelBuilder.Entity<ProductAlias>().HasIndex(x => x.NormalizedValue);
        modelBuilder.Entity<ProductAlias>().HasOne(x => x.Product).WithMany(x => x.Aliases)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkOrderItem>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkOrderItem>()
            .ToTable(t => t.HasCheckConstraint("CK_WorkOrderItems_Quantity_Positive", "\"Quantity\" > 0"));

        modelBuilder.Entity<WorkOrderItem>()
            .HasOne(x => x.WorkOrder)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkOrder>()
            .Property(x => x.DueDate)
            .HasColumnType("date");

        modelBuilder.Entity<ConstructionSite>()
            .Property(x => x.CompletedDate)
            .HasColumnType("date");
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ICompanyRecord>().Where(x => x.State == EntityState.Added))
            entry.Entity.CompanyKey = _company.Key;
        return base.SaveChangesAsync(cancellationToken);
    }
}
