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

    public static readonly string[] HistoryStatuses = Enum.GetValues<OrderStatus>().Select(AdminDisplay.OrderStatusLabel).ToArray();

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public OrderModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => _clock.Today;

    public record AdvanceRequest(string Id, string? ExpectedStatus);

    public record OrderRow(
        string Id,
        string DisplayId,
        string? Table,
        string Customer,
        List<string> Items,
        DateTime CreatedAt,
        int Total,
        string Status);

    public async Task<IActionResult> OnGetListAsync()
    {
        var activeOrders = _db.Orders.Where(o => ActiveStatuses.Contains(o.Status));
        return new JsonResult(await ToRowsAsync(activeOrders));
    }

    public async Task<IActionResult> OnGetHistoryAsync(string? from, string? to, string? status)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }

        var startUtc = _clock.StartOfDayUtc(fromDate);
        var endUtc = _clock.StartOfDayUtc(toDate.AddDays(1));
        var orders = _db.Orders.Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc);

        if (!string.IsNullOrEmpty(status) && status != "All")
        {
            var matches = Enum.GetValues<OrderStatus>().Where(s => AdminDisplay.OrderStatusLabel(s) == status).ToList();
            if (matches.Count == 0)
            {
                return BadRequest(new { error = "Please choose a valid status." });
            }
            var orderStatus = matches[0];
            orders = orders.Where(o => o.Status == orderStatus);
        }

        return new JsonResult(await ToRowsAsync(orders));
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
        var items = (await _db.OrderItems.AsNoTracking()
                .Where(oi => orderIds.Contains(oi.OrderId))
                .Select(oi => new { oi.OrderId, oi.MenuItem!.Name, oi.Quantity })
                .ToListAsync())
            .ToLookup(oi => oi.OrderId, oi => $"{oi.Name} x{oi.Quantity}");

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
