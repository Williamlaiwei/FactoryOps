using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Services;

public class InventoryException(string message) : Exception(message);

// Mutations join the caller's one SaveChanges transaction; no inventory-only commit.
public class InventoryService(FactoryOpsDbContext db)
{
    public async Task SyncOrder(WorkOrder order, bool wasCompleted)
    {
        var previous = await db.StockMovements.Where(x => x.WorkOrderId == order.Id &&
            (x.Kind == "Order issue" || x.Kind == "Order return")).ToListAsync();
        // An opening physical count already includes history. Do not retroactively issue old completed orders.
        if (wasCompleted && previous.Count == 0) return;
        var stocks = await db.InventoryStocks.Include(x => x.Product).ToDictionaryAsync(x => x.ProductId);
        var desired = new Dictionary<int, long>();
        if (order.Status == "Completed")
        {
            foreach (var group in order.Items.Where(x => db.Entry(x).State != EntityState.Deleted).GroupBy(x => x.ProductId))
            {
                if (!group.Key.HasValue || !stocks.TryGetValue(group.Key.Value, out var stock)) continue;
                if (group.Any(x => !InventoryPlanner.CompatibleUnit(x.UnitOfMeasureSnapshot, stock.Product.UnitOfMeasure)))
                    throw new InventoryException($"Cannot complete this order: {stock.Product.Sku} has a missing or different ordering unit. Review its product lines first.");
                desired[stock.ProductId] = -group.Sum(x => (long)x.Quantity);
            }
        }
        var posted = previous.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(m => m.Quantity));
        foreach (var productId in desired.Keys.Union(posted.Keys))
        {
            var delta = desired.GetValueOrDefault(productId) - posted.GetValueOrDefault(productId);
            if (delta == 0) continue;
            var stock = stocks[productId];
            var next = stock.OnHand + delta;
            if (next < 0) throw new InventoryException($"Not enough stock to complete this order: {stock.Product.Sku} has {stock.OnHand} {stock.Product.UnitOfMeasure}; {-delta} required now. Receive stock or correct the order first.");
            if (next > StockSettingsForm.MaximumQuantity) throw new InventoryException("The resulting stock count is too large. Review the inventory count.");
            stock.OnHand = next;
            stock.UpdatedAt = DateTimeOffset.UtcNow;
            db.StockMovements.Add(new()
            {
                ProductId = productId, Quantity = delta, Kind = delta < 0 ? "Order issue" : "Order return",
                WorkOrderId = order.Id, Reason = $"Order WO-{order.Id}: {order.Status}"
            });
        }
    }
}
