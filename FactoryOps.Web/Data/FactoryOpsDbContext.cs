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
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<Replenishment> Replenishments => Set<Replenishment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
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
        foreach (var property in new[] { nameof(Product.PackingSize), nameof(Product.RoughIn), nameof(Product.HeatingMethod),
            nameof(Product.FlushingMethod), nameof(Product.CeramicBody), nameof(Product.Color), nameof(Product.DisplayMode),
            nameof(Product.NetWeight), nameof(Product.GrossWeight) })
            modelBuilder.Entity<Product>().Property<string>(property).HasDefaultValue("");
        foreach (var type in new[] { typeof(Product), typeof(Customer), typeof(ConstructionSite), typeof(WorkOrder), typeof(InventoryStock), typeof(Replenishment), typeof(StockMovement), typeof(StockReservation) })
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
        modelBuilder.Entity<InventoryStock>().HasKey(x => x.ProductId);
        modelBuilder.Entity<InventoryStock>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<Replenishment>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<InventoryStock>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<Replenishment>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<StockMovement>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<StockReservation>().HasQueryFilter(x => x.CompanyKey == _company.Key);
        modelBuilder.Entity<StockReservation>().HasKey(x => new { x.WorkOrderId, x.ProductId });
        modelBuilder.Entity<StockReservation>().HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockReservation>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockReservation>().Property(x => x.NeedDate).HasColumnType("date");
        modelBuilder.Entity<StockReservation>().HasIndex(x => new { x.CompanyKey, x.ProductId, x.NeedDate });
        modelBuilder.Entity<StockReservation>().ToTable(t => t.HasCheckConstraint("CK_StockReservations_Values", "\"Quantity\" > 0 AND \"Status\" IN ('Held','Shipped','Released')"));
        modelBuilder.Entity<WorkOrder>().Property(x => x.FulfillmentStatus).HasMaxLength(24).HasDefaultValue("Unconfirmed");
        modelBuilder.Entity<WorkOrder>().HasIndex(x => new { x.CompanyKey, x.CreationToken }).IsUnique();
        modelBuilder.Entity<InventoryStock>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Replenishment>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>().HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>().HasOne(x => x.Replenishment).WithMany().HasForeignKey(x => x.ReplenishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Replenishment>().Property(x => x.ExpectedDate).HasColumnType("date");
        modelBuilder.Entity<Replenishment>().HasIndex(x => new { x.CompanyKey, x.CreationToken }).IsUnique();
        modelBuilder.Entity<Replenishment>().HasIndex(x => new { x.CompanyKey, x.ExpectedDate });
        modelBuilder.Entity<StockMovement>().HasIndex(x => new { x.CompanyKey, x.RequestToken }).IsUnique();
        modelBuilder.Entity<StockMovement>().HasIndex(x => new { x.CompanyKey, x.ProductId, x.CreatedAt });
        modelBuilder.Entity<StockMovement>().HasIndex(x => x.WorkOrderId);
        modelBuilder.Entity<InventoryStock>().ToTable(t => t.HasCheckConstraint("CK_InventoryStocks_Values", "\"OnHand\" BETWEEN 0 AND 1000000000000 AND \"ReorderPoint\" BETWEEN 0 AND 1000000000000 AND \"LeadTimeDays\" BETWEEN 0 AND 3650"));
        modelBuilder.Entity<Replenishment>().ToTable(t => t.HasCheckConstraint("CK_Replenishments_Values", "\"Quantity\" BETWEEN 1 AND 1000000000000 AND \"Status\" IN ('Planned','Received','Cancelled')"));
        modelBuilder.Entity<StockMovement>().ToTable(t => t.HasCheckConstraint("CK_StockMovements_Nonzero", "\"Quantity\" <> 0"));
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
