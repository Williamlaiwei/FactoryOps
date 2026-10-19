using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FactoryOps.Web.Services;

public class InventoryException(string message) : Exception(message) { }

// All stock/hold mutations use one company transaction lock, then one atomic save.
public class InventoryService(FactoryOpsDbContext db, CompanyContext company, BusinessClock clock)
{
    public async Task<IDbContextTransaction> BeginMutation()
    {
        var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var key = "factoryops-inventory:" + company.Key;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0));");
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private Dictionary<int, (long Quantity, string Unit)> Describe(WorkOrder order)
    {
        var items = order.Items.Where(x => db.Entry(x).State != EntityState.Deleted).ToList();
        if (items.Count == 0 || items.Any(x => !x.ProductId.HasValue || x.Quantity <= 0))
            throw new InventoryException("Review and link every product line before confirming inventory holds.");
        var result = new Dictionary<int, (long, string)>();
        foreach (var group in items.GroupBy(x => x.ProductId!.Value))
        {
            var unit = group.First().UnitOfMeasureSnapshot;
            if (group.Any(x => !InventoryPlanner.CompatibleUnit(x.UnitOfMeasureSnapshot, unit)))
                throw new InventoryException("Review product units before confirming. Explicitly confirm the current catalog units when correcting old lines.");
            result[group.Key] = (group.Sum(x => (long)x.Quantity), unit);
        }
        return result;
    }

    public async Task Reserve(WorkOrder order)
    {
        if (order.FulfillmentStatus is "Shipped" or "Historical")
            throw new InventoryException("Shipped or historical orders cannot create new stock holds.");
        var desired = Describe(order);
        await CheckCoverage(order.Id, order.DueDate.Date, desired, new());
        var rows = order.Id == 0 ? new List<StockReservation>() : await db.StockReservations.Where(x => x.WorkOrderId == order.Id).ToListAsync();
        foreach (var row in rows.Where(x => x.Status == "Held" && !desired.ContainsKey(x.ProductId))) row.Status = "Released";
        foreach (var (id, value) in desired)
        {
            var row = rows.SingleOrDefault(x => x.ProductId == id);
            if (row == null) { row = new StockReservation { WorkOrder = order, ProductId = id }; db.StockReservations.Add(row); }
            row.Quantity = value.Quantity; row.UnitOfMeasure = value.Unit; row.NeedDate = order.DueDate.Date; row.Status = "Held";
        }
        order.FulfillmentStatus = "Reserved";
    }

    public async Task Cancel(WorkOrder order)
    {
        if (order.FulfillmentStatus is "Shipped" or "Historical")
            throw new InventoryException("A shipped or historical order cannot be cancelled to return stock. Record an actual return separately.");
        foreach (var row in await db.StockReservations.Where(x => x.WorkOrderId == order.Id && x.Status == "Held").ToListAsync()) row.Status = "Released";
        order.FulfillmentStatus = "Cancelled";
    }

    public async Task Ship(WorkOrder order)
    {
        if (order.FulfillmentStatus == "Shipped") return;
        if (order.FulfillmentStatus != "Reserved") throw new InventoryException("Confirm inventory holds before shipping this order.");
        var desired = Describe(order);
        var held = await db.StockReservations.Where(x => x.WorkOrderId == order.Id && x.Status == "Held").ToListAsync();
        if (held.Count != desired.Count || held.Any(x => !desired.TryGetValue(x.ProductId, out var value) || value.Quantity != x.Quantity || !InventoryPlanner.CompatibleUnit(x.UnitOfMeasure, value.Unit)))
            throw new InventoryException("Order lines and holds differ. Review and reconfirm the order before shipping.");
        var issued = desired.ToDictionary(x => x.Key, x => x.Value.Quantity);
        await CheckCoverage(order.Id, order.DueDate.Date, new(), issued);
        var stocks = await db.InventoryStocks.Where(x => issued.Keys.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId);
        foreach (var row in held)
        {
            var stock = stocks[row.ProductId];
            stock.OnHand -= row.Quantity; stock.UpdatedAt = DateTimeOffset.UtcNow;
            row.Status = "Shipped";
            db.StockMovements.Add(new()
            {
                ProductId = row.ProductId, Quantity = -row.Quantity, Kind = "Shipment", WorkOrderId = order.Id,
                Reason = $"Dispatched order WO-{order.Id}"
            });
        }
        order.FulfillmentStatus = "Shipped"; order.ShippedAt = DateTimeOffset.UtcNow;
    }

    private async Task CheckCoverage(int orderId, DateTime needDate, Dictionary<int, (long Quantity, string Unit)> proposed, Dictionary<int, long> shipping)
    {
        var today = clock.Today;
        var held = await db.StockReservations.AsNoTracking().Where(x => x.Status == "Held").ToListAsync();
        var stocks = await db.InventoryStocks.AsNoTracking().Include(x => x.Product).ToDictionaryAsync(x => x.ProductId);
        var inbound = await db.Replenishments.AsNoTracking().Where(x => x.Status == "Planned" && x.ExpectedDate >= today).ToListAsync();
        DateTime Effective(DateTime value) => value.Date < today ? today : value.Date;
        foreach (var productId in proposed.Keys.Union(shipping.Keys))
        {
            if (!stocks.TryGetValue(productId, out var stock))
                throw new InventoryException($"Stock is not tracked for product #{productId}. Enter its physical count in Inventory before confirming this order.");
            var product = stock.Product;
            if (proposed.TryGetValue(productId, out var item) && !InventoryPlanner.CompatibleUnit(item.Unit, product.UnitOfMeasure))
                throw new InventoryException($"{product.Sku}: missing or different ordering unit. Review quantities and confirm the current catalog units.");
            var holds = held.Where(x => x.ProductId == productId).ToList();
            if (holds.Any(x => !InventoryPlanner.CompatibleUnit(x.UnitOfMeasure, product.UnitOfMeasure)))
                throw new InventoryException($"{product.Sku}: an existing hold has a different unit. Review it before allocating more stock.");
            if (shipping.GetValueOrDefault(productId) > stock.OnHand)
                throw new InventoryException($"Not enough actual stock to ship {product.Sku}: {stock.OnHand} {product.UnitOfMeasure} on hand. Receive the incoming goods first.");
            var receipts = inbound.Where(x => x.ProductId == productId).ToList();
            var dates = holds.Select(x => Effective(x.NeedDate)).Concat(receipts.Select(x => x.ExpectedDate.Date))
                .Append(today).Append(Effective(needDate)).Distinct().OrderBy(x => x);
            var before = stock.OnHand;
            var after = stock.OnHand - shipping.GetValueOrDefault(productId);
            foreach (var date in dates)
            {
                var supply = receipts.Where(x => x.ExpectedDate.Date == date).Sum(x => x.Quantity);
                before += supply - holds.Where(x => Effective(x.NeedDate) == date).Sum(x => x.Quantity);
                after += supply - holds.Where(x => x.WorkOrderId != orderId && Effective(x.NeedDate) == date).Sum(x => x.Quantity);
                if (proposed.TryGetValue(productId, out var demand) && Effective(needDate) == date) after -= demand.Quantity;
                if (after < 0 && after < before)
                    throw new InventoryException($"Cannot confirm or ship: {product.Sku} would be short {-after} {product.UnitOfMeasure} on {date:yyyy-MM-dd} after existing holds. Reduce the quantity or schedule sufficient incoming stock before that date.");
            }
        }
    }
}
