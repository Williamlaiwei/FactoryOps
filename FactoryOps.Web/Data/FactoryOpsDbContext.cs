using Microsoft.EntityFrameworkCore;
using FactoryOps.Web.Models;

namespace FactoryOps.Web.Data;

public class FactoryOpsDbContext : DbContext
{
    public FactoryOpsDbContext(
        DbContextOptions<FactoryOpsDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkOrder> WorkOrders
        => Set<WorkOrder>();
    public DbSet<Customer> Customers
    => Set<Customer>();

    public DbSet<ConstructionSite> ConstructionSites
        => Set<ConstructionSite>();
    public DbSet<WorkOrderItem> WorkOrderItems => Set<WorkOrderItem>();
    protected override void OnModelCreating(
    ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrder>()
            .Property(x => x.DueDate)
            .HasColumnType("date");

        modelBuilder.Entity<ConstructionSite>()
            .Property(x => x.CompletedDate)
            .HasColumnType("date");
    }
}