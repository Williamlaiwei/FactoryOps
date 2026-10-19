using FactoryOps.Web.Data;
using FactoryOps.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FactoryOps.Web.Services;

public class InventoryPlanner(FactoryOpsDbContext db, BusinessClock clock)
{
    public async Task<InventoryPlan> Build()
    {
        await using var snapshot = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var plan = new InventoryPlan { Today = clock.Today };
        var stocks = await db.InventoryStocks.AsNoTracking().ToDictionaryAsync(x => x.ProductId);
        var products = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync();
        var byId = products.ToDictionary(x => x.Id, x => new InventoryProductPlan { Product = x, Stock = stocks.GetValueOrDefault(x.Id) });
        plan.Products = byId.Values.ToList();
        void Warn(int orderId, string message)
        {
            if (!plan.OrderWarnings.TryGetValue(orderId, out var messages)) plan.OrderWarnings[orderId] = messages = new();
            if (!messages.Contains(message)) messages.Add(message);
        }
        foreach (var order in await db.WorkOrders.AsNoTracking().Where(x => x.FulfillmentStatus == "Unconfirmed").ToListAsync())
            Warn(order.Id, "Inventory holds are not confirmed. Review the product lines and confirm this order.");
        foreach (var hold in await db.StockReservations.AsNoTracking().Where(x => x.Status == "Held").ToListAsync())
        {
            if (!byId.TryGetValue(hold.ProductId, out var product)) { Warn(hold.WorkOrderId, "A held product requires inventory review."); continue; }
            var known = product.Stock != null && CompatibleUnit(hold.UnitOfMeasure, product.Product.UnitOfMeasure);
            if (!known) { product.UncertainDemand++; Warn(hold.WorkOrderId, $"{product.Product.Sku}: stock not tracked or units need review."); }
            product.Events.Add(new()
            {
                Product = product.Product, Date = hold.NeedDate.Date < plan.Today ? plan.Today : hold.NeedDate.Date,
                ScheduledDate = hold.NeedDate.Date, Kind = "Outbound hold", SourceId = hold.WorkOrderId,
                Outgoing = hold.Quantity, CountsInProjection = known, ProjectionKnown = known
            });
        }
        foreach (var receipt in await db.Replenishments.AsNoTracking().Where(x => x.Status == "Planned").ToListAsync())
        {
            if (!byId.TryGetValue(receipt.ProductId, out var product) || product.Stock == null) continue;
            product.Events.Add(new()
            {
                Product = product.Product, Date = receipt.ExpectedDate.Date < plan.Today ? plan.Today : receipt.ExpectedDate.Date,
                ScheduledDate = receipt.ExpectedDate.Date, Kind = "Incoming hold", SourceId = receipt.Id,
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
                if (item.Outgoing > 0 && item.ProjectionKnown)
                {
                    withoutReceipts -= item.Outgoing;
                    if (balance < 0) Warn(item.SourceId, $"{product.Product.Sku}: short {-balance} {product.Product.UnitOfMeasure} by {item.Date:yyyy-MM-dd}.");
                    else if (withoutReceipts < 0) Warn(item.SourceId, $"{product.Product.Sku}: depends on planned receipts arriving on time.");
                }
            }
            if (reorderDate.HasValue) product.ReorderBy = reorderDate.Value.AddDays(-stock.LeadTimeDays);
        }
        if (snapshot != null) await snapshot.CommitAsync();
        return plan;
    }

    public static bool CompatibleUnit(string snapshot, string current) => !string.IsNullOrWhiteSpace(snapshot) &&
        snapshot.Trim().Equals(current.Trim(), StringComparison.OrdinalIgnoreCase);

    public static StockProjection Project(InventoryPlan plan, InventoryProductPlan product, int months, string companyKey)
    {
        var end = plan.Today.AddMonths(months);
        var result = new StockProjection
        {
            CompanyKey = companyKey, ProductId = product.Product.Id, Sku = product.Product.Sku, Name = product.Product.Name,
            Unit = product.Product.UnitOfMeasure, StartDate = plan.Today.ToString("yyyy-MM-dd"), EndDate = end.ToString("yyyy-MM-dd"),
            Months = months, OnHand = product.Stock?.OnHand, OutboundHeld = product.OpenDemand,
            IncomingHeld = product.ExpectedInbound, OverdueReceipts = product.OverdueReceipts, NeedsReview = product.UncertainDemand > 0
        };
        if (product.Stock == null) return result;
        long incoming = 0, outgoing = 0;
        var events = product.Events.Where(x => x.CountsInProjection).GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());
        for (var day = plan.Today; day <= end; day = day.AddDays(1))
        {
            var dated = events.GetValueOrDefault(day) ?? new();
            var supply = dated.Sum(x => x.Incoming); var demand = dated.Sum(x => x.Outgoing);
            incoming += supply; outgoing += demand;
            result.Days.Add(new() { Date = day.ToString("yyyy-MM-dd"), Actual = product.Stock.OnHand,
                WithIncoming = product.Stock.OnHand + incoming, Available = product.Stock.OnHand + incoming - outgoing,
                Incoming = supply, Outgoing = demand });
        }
        return result;
    }
}
