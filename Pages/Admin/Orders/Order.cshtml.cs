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

    public OrderModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public record AdvanceRequest(string Id, string? ExpectedStatus);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => ActiveStatuses.Contains(o.Status))
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

        return new JsonResult(orders.Select(o => new
        {
            id = o.Id,
            displayId = AdminDisplay.ShortId(o.Id),
            table = o.TableId,
            customer = o.CustomerEmail,
            items = items[o.Id],
            createdAt = o.CreatedAt,
            total = o.TotalAmount,
            status = AdminDisplay.OrderStatus(o.Status)
        }));
    }

    public async Task<IActionResult> OnPostAdvanceAsync([FromBody] AdvanceRequest request)
    {
        var order = await _db.Orders.FindAsync(request.Id);
        if (order is null)
        {
            return NotFound(new { error = "This order no longer exists. Refresh the page." });
        }

        var current = AdminDisplay.OrderStatus(order.Status);
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

        return new JsonResult(new { id = order.Id, status = AdminDisplay.OrderStatus(order.Status) });
    }
}
