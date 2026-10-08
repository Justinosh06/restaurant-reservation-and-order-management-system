using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin;

public class IndexModel : PageModel
{
    private const int DefaultFirstHour = 10;
    private const int DefaultLastHour = 22;

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public IndexModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetStatsAsync()
    {
        var today = _clock.Today;
        var yesterday = today.AddDays(-1);
        var weekStart = today.AddDays(-6);
        var weekStartUtc = _clock.StartOfDayUtc(weekStart);

        var orders = (await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= weekStartUtc && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.TotalAmount, o.Status, o.CreatedAt })
                .ToListAsync())
            .Select(o =>
            {
                var local = _clock.ToLocal(o.CreatedAt);
                return new { o.TotalAmount, o.Status, Day = DateOnly.FromDateTime(local), local.Hour };
            })
            .ToList();

        var ordersToday = orders.Where(o => o.Day == today).ToList();
        var ordersYesterday = orders.Where(o => o.Day == yesterday).ToList();

        var reservationsToday = await CountReservationsAsync(today);
        var reservationsYesterday = await CountReservationsAsync(yesterday);

        var revenueLast7Days = Enumerable.Range(0, 7)
            .Select(offset => weekStart.AddDays(offset))
            .Select(day => new
            {
                date = day.ToString("yyyy-MM-dd"),
                amount = orders.Where(o => o.Day == day).Sum(o => o.TotalAmount)
            });

        var ordersByStatus = new[]
        {
            new { status = "Pending", count = ordersToday.Count(o => o.Status == OrderStatus.InQueue) },
            new { status = "Preparing", count = ordersToday.Count(o => o.Status == OrderStatus.Preparing) },
            new { status = "Served", count = ordersToday.Count(o => o.Status is OrderStatus.Served or OrderStatus.Completed) }
        };

        var (firstHour, lastHour) = await GetOpeningHoursAsync(today.DayOfWeek);
        if (ordersToday.Count > 0)
        {
            firstHour = Math.Min(firstHour, ordersToday.Min(o => o.Hour));
            lastHour = Math.Max(lastHour, ordersToday.Max(o => o.Hour));
        }
        var ordersByHour = Enumerable.Range(firstHour, lastHour - firstHour + 1)
            .Select(hour => new { hour, count = ordersToday.Count(o => o.Hour == hour) });

        var topItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Order!.CreatedAt >= weekStartUtc && oi.Order.Status != OrderStatus.Cancelled)
            .GroupBy(oi => oi.MenuItem!.Name)
            .Select(g => new { name = g.Key, units = g.Sum(oi => oi.Quantity) })
            .OrderByDescending(x => x.units)
            .Take(5)
            .ToListAsync();

        var recentOrders = (await _db.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new { o.Id, o.TotalAmount, o.Status, o.CreatedAt, o.Session!.TableId })
                .ToListAsync())
            .Select(o => new
            {
                id = AdminDisplay.ShortId(o.Id),
                table = o.TableId,
                createdAt = o.CreatedAt,
                total = o.TotalAmount,
                status = AdminDisplay.OrderStatus(o.Status)
            });

        return new JsonResult(new
        {
            revenueToday = ordersToday.Sum(o => o.TotalAmount),
            revenueYesterday = ordersYesterday.Sum(o => o.TotalAmount),
            ordersToday = ordersToday.Count,
            ordersYesterday = ordersYesterday.Count,
            reservationsToday,
            reservationsYesterday,
            revenueLast7Days,
            ordersByStatus,
            ordersByHour,
            topItems,
            recentOrders
        });
    }

    private Task<int> CountReservationsAsync(DateOnly date) =>
        _db.Reservations.CountAsync(r => r.Date == date && r.Status != ReservationStatus.Cancelled);

    private async Task<(int First, int Last)> GetOpeningHoursAsync(DayOfWeek day)
    {
        var hours = await _db.BusinessHours.AsNoTracking()
            .Where(h => h.DayOfWeek == day && !h.IsClosed)
            .Select(h => new { h.OpenTime, h.CloseTime })
            .FirstOrDefaultAsync();

        if (hours?.OpenTime is not { } open || hours.CloseTime is not { } close)
        {
            return (DefaultFirstHour, DefaultLastHour);
        }

        var last = close.Minute == 0 ? close.Hour - 1 : close.Hour;
        return (open.Hour, Math.Max(open.Hour, last));
    }
}
