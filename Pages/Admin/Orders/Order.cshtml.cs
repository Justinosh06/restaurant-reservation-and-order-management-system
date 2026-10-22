using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Orders;

public class OrderModel : PageModel
{
    private static readonly OrderStatus[] ActiveStatuses = [OrderStatus.InQueue, OrderStatus.Preparing, OrderStatus.Served];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public OrderModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => _clock.Today;

    // null when today's opening hours haven't been set in Restaurant Settings.
    public bool? IsOpenNow { get; private set; }

    public async Task OnGetAsync()
    {
        var now = _clock.Now;
        var hours = await _db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(h => h.DayOfWeek == now.DayOfWeek);
        if (hours is null)
        {
            return;
        }

        if (hours.IsClosed || hours.OpenTime is null || hours.CloseTime is null)
        {
            IsOpenNow = false;
            return;
        }

        var time = TimeOnly.FromDateTime(now);
        IsOpenNow = time.IsBetween(hours.OpenTime.Value, hours.CloseTime.Value);
    }

    public record AdvanceRequest(string Id, string? ExpectedStatus);

    public record OrderItemRow(string Name, int Quantity, int UnitPrice, string? Remarks);

    public record OrderRow(
        string Id,
        string DisplayId,
        string? Table,
        string Customer,
        List<OrderItemRow> Items,
        DateTime CreatedAt,
        int Total,
        string Status);

    public async Task<IActionResult> OnGetListAsync()
    {
        var activeOrders = _db.Orders.Where(o => ActiveStatuses.Contains(o.Status));
        return new JsonResult(await ToRowsAsync(activeOrders));
    }

    public record VisitRow(
        string Id,
        string? Table,
        string Customer,
        List<string> OrderIds,
        List<OrderItemRow> Items,
        DateTime StartedAt,
        DateTime LastOrderAt,
        int Total);

    // History only lists finished orders: served to the table and paid for (status Completed).
    // Orders are grouped by dining session, so a table that ordered twice in one visit shows as one bill.
    public async Task<IActionResult> OnGetHistoryAsync(string? from, string? to)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }

        var startUtc = _clock.StartOfDayUtc(fromDate);
        var endUtc = _clock.StartOfDayUtc(toDate.AddDays(1));
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .Select(o => new
            {
                o.Id,
                o.SessionId,
                o.TotalAmount,
                o.CreatedAt,
                o.Session!.TableId,
                OrderEmail = o.Customer!.Email,
                SessionEmail = o.Session.Customer!.Email
            })
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var orderItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => orderIds.Contains(oi.OrderId))
            .Select(oi => new { oi.OrderId, oi.MenuItemId, oi.MenuItem!.Name, oi.MenuItem.Price, oi.Quantity, oi.Remarks })
            .ToListAsync();
        var itemsByOrder = orderItems.ToLookup(oi => oi.OrderId);

        var visits = orders
            .GroupBy(o => o.SessionId)
            .Select(visit =>
            {
                var visitOrders = visit.OrderBy(o => o.CreatedAt).ToList();
                var visitItems = visitOrders.SelectMany(o => itemsByOrder[o.Id]).ToList();

                // The same dish ordered in both rounds becomes one line with the quantities added up.
                var items = visitItems
                    .GroupBy(oi => oi.MenuItemId)
                    .Select(dish => new OrderItemRow(
                        dish.First().Name,
                        dish.Sum(oi => oi.Quantity),
                        dish.First().Price,
                        JoinRemarks(dish.Select(oi => oi.Remarks))))
                    .ToList();

                var customers = visitOrders
                    .Select(o => o.OrderEmail ?? o.SessionEmail)
                    .Where(email => !string.IsNullOrEmpty(email))
                    .Distinct()
                    .ToList();

                return new VisitRow(
                    visit.Key,
                    visitOrders[0].TableId,
                    customers.Count == 0 ? "Walk-in guest" : string.Join(", ", customers),
                    visitOrders.Select(o => AdminDisplay.ShortId(o.Id)).ToList(),
                    items,
                    visitOrders[0].CreatedAt,
                    visitOrders[^1].CreatedAt,
                    visitOrders.Sum(o => o.TotalAmount));
            })
            .OrderByDescending(v => v.LastOrderAt)
            .ToList();

        return new JsonResult(visits);
    }

    private static string? JoinRemarks(IEnumerable<string?> remarks)
    {
        var distinct = remarks.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()).Distinct().ToList();
        return distinct.Count == 0 ? null : string.Join("; ", distinct);
    }

    private async Task<List<OrderRow>> ToRowsAsync(IQueryable<Order> query)
    {
        var orders = await query.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.TotalAmount,
                o.Status,
                o.CreatedAt,
                o.Session!.TableId,
                CustomerEmail = o.Customer!.Email
            })
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var orderItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => orderIds.Contains(oi.OrderId))
            .Select(oi => new { oi.OrderId, oi.MenuItem!.Name, oi.MenuItem.Price, oi.Quantity, oi.Remarks })
            .ToListAsync();
        var items = orderItems.ToLookup(oi => oi.OrderId, oi => new OrderItemRow(oi.Name, oi.Quantity, oi.Price, oi.Remarks));

        return orders.Select(o => new OrderRow(
            o.Id,
            AdminDisplay.ShortId(o.Id),
            o.TableId,
            o.CustomerEmail,
            items[o.Id].ToList(),
            o.CreatedAt,
            o.TotalAmount,
            AdminDisplay.OrderStatusLabel(o.Status))).ToList();
    }

    public async Task<IActionResult> OnPostAdvanceAsync([FromBody] AdvanceRequest request)
    {
        var order = await _db.Orders.FindAsync(request.Id);
        if (order is null)
        {
            return NotFound(new { error = "This order no longer exists. Refresh the page." });
        }

        var current = AdminDisplay.OrderStatusLabel(order.Status);
        if (current != request.ExpectedStatus)
        {
            return new ConflictObjectResult(new { error = $"This order was already moved to {current} by someone else.", id = order.Id, status = current });
        }

        OrderStatus? next = order.Status switch
        {
            OrderStatus.InQueue => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.Served,
            _ => null
        };

        if (next is null)
        {
            return BadRequest(new { error = "This order is already served." });
        }

        order.Status = next.Value;
        if (order.Status == OrderStatus.Served)
        {
            order.ServedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        return new JsonResult(new { id = order.Id, status = AdminDisplay.OrderStatusLabel(order.Status) });
    }
}
