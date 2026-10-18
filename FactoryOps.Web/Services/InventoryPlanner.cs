using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Services;

public class InventoryPlanner(FactoryOpsDbContext db)
{
    public async Task<InventoryPlan> Build()
    {
        var plan = new InventoryPlan();
        var stocks = await db.InventoryStocks.AsNoTracking().ToDictionaryAsync(x => x.ProductId);
        var products = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync();
        var byId = products.ToDictionary(x => x.Id, x => new InventoryProductPlan { Product = x, Stock = stocks.GetValueOrDefault(x.Id) });
        plan.Products = byId.Values.ToList();
        void Warn(int orderId, string message)
        {
            if (!plan.OrderWarnings.TryGetValue(orderId, out var messages)) plan.OrderWarnings[orderId] = messages = new();
            if (!messages.Contains(message)) messages.Add(message);
        }
        var orders = await db.WorkOrders.AsNoTracking().Include(x => x.Items).Where(x => x.Status != "Completed" && x.Status != "Cancelled").ToListAsync();
        foreach (var order in orders)
        {
            if (order.Items.Count == 0) Warn(order.Id, "Unlinked legacy product: inventory review required.");
            foreach (var group in order.Items.GroupBy(x => x.ProductId))
            {
                if (!group.Key.HasValue || !byId.TryGetValue(group.Key.Value, out var product))
                { Warn(order.Id, "Unlinked product: inventory review required."); continue; }
                if (product.Stock == null)
                { Warn(order.Id, $"{product.Product.Sku}: stock not tracked."); continue; }
                if (group.Any(x => !CompatibleUnit(x.UnitOfMeasureSnapshot, product.Product.UnitOfMeasure)))
                {
                    product.UncertainDemand++;
                    Warn(order.Id, $"{product.Product.Sku}: missing or different ordering unit; review quantities.");
                    continue;
                }
                product.Events.Add(new()
                {
                    Product = product.Product, Date = order.DueDate.Date < plan.Today ? plan.Today : order.DueDate.Date,
                    ScheduledDate = order.DueDate.Date, Kind = "Order", SourceId = order.Id, Outgoing = group.Sum(x => (long)x.Quantity)
                });
            }
        }
        foreach (var receipt in await db.Replenishments.AsNoTracking().Where(x => x.Status == "Planned").ToListAsync())
        {
            if (!byId.TryGetValue(receipt.ProductId, out var product) || product.Stock == null) continue;
            product.Events.Add(new()
            {
                Product = product.Product, Date = receipt.ExpectedDate.Date < plan.Today ? plan.Today : receipt.ExpectedDate.Date,
                ScheduledDate = receipt.ExpectedDate.Date, Kind = "Expected receipt", SourceId = receipt.Id,
                Incoming = receipt.Quantity, CountsInProjection = receipt.ExpectedDate.Date >= plan.Today
            });
        }
        foreach (var product in plan.Products.Where(x => x.Stock != null))
        {
            var stock = product.Stock!;
            var balance = stock.OnHand;
            var withoutReceipts = stock.OnHand;
            product.MinimumBalance = balance;
            DateTime? reorderDate = balance <= stock.ReorderPoint ? plan.Today : null;
            product.Events = product.Events.OrderBy(x => x.Date).ThenBy(x => x.Outgoing > 0 ? 1 : 0).ThenBy(x => x.SourceId).ToList();
            foreach (var item in product.Events)
            {
                if (item.CountsInProjection) balance += item.Incoming - item.Outgoing;
                item.Balance = balance;
                product.MinimumBalance = Math.Min(product.MinimumBalance, balance);
                if (balance <= stock.ReorderPoint) reorderDate ??= item.Date;
                if (balance < 0) product.FirstShortageDate ??= item.Date;
                if (item.Outgoing > 0)
                {
                    withoutReceipts -= item.Outgoing;
                    if (balance < 0) Warn(item.SourceId, $"{product.Product.Sku}: short {-balance} {product.Product.UnitOfMeasure} by {item.Date:yyyy-MM-dd}.");
                    else if (withoutReceipts < 0) Warn(item.SourceId, $"{product.Product.Sku}: depends on planned receipts arriving on time.");
                }
            }
            if (reorderDate.HasValue) product.ReorderBy = reorderDate.Value.AddDays(-stock.LeadTimeDays);
        }
        return plan;
    }

    public static bool CompatibleUnit(string snapshot, string current) => !string.IsNullOrWhiteSpace(snapshot) &&
        snapshot.Trim().Equals(current.Trim(), StringComparison.OrdinalIgnoreCase);
}
