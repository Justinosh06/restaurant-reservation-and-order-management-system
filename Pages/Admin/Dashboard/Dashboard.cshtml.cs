using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Dashboard;

public class DashboardModel : PageModel
{
    private const int DefaultFirstHour = 10;
    private const int DefaultLastHour = 22;

    private static readonly OrderStatus[] RevenueStatuses = [OrderStatus.Served, OrderStatus.Completed];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public DashboardModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => _clock.Today;

    public async Task<IActionResult> OnGetStatsAsync(string? from, string? to)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }

        // The previous period has the same number of days and ends the day before the chosen range.
        var dayCount = toDate.DayNumber - fromDate.DayNumber + 1;
        var previousFrom = fromDate.AddDays(-dayCount);
        var days = Enumerable.Range(0, dayCount).Select(i => fromDate.AddDays(i)).ToList();

        var startUtc = _clock.StartOfDayUtc(previousFrom);
        var endUtc = _clock.StartOfDayUtc(toDate.AddDays(1));

        var placedOrders = (await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc && o.Status != OrderStatus.Cancelled)
                .Select(o => o.CreatedAt)
                .ToListAsync())
            .Select(createdAt => _clock.ToLocal(createdAt))
            .Select(local => new { Day = DateOnly.FromDateTime(local), local.Hour })
            .ToList();

        var servedOrders = (await _db.Orders.AsNoTracking()
                .Where(o => RevenueStatuses.Contains(o.Status))
                .Where(o => (o.ServedAt ?? o.CreatedAt) >= startUtc && (o.ServedAt ?? o.CreatedAt) < endUtc)
                .Select(o => new { o.TotalAmount, ServedAt = o.ServedAt ?? o.CreatedAt })
                .ToListAsync())
            .Select(o => new { o.TotalAmount, Day = DateOnly.FromDateTime(_clock.ToLocal(o.ServedAt)) })
            .ToList();

        var orders = placedOrders.Where(o => o.Day >= fromDate).ToList();
        var previousOrders = placedOrders.Where(o => o.Day < fromDate).ToList();
        var served = servedOrders.Where(o => o.Day >= fromDate).ToList();
        var previousServed = servedOrders.Where(o => o.Day < fromDate).ToList();

        var revenue = served.Sum(o => o.TotalAmount);
        var previousRevenue = previousServed.Sum(o => o.TotalAmount);

        var reservations = await _db.Reservations.CountAsync(r =>
            r.Date >= fromDate && r.Date <= toDate && r.Status != ReservationStatus.Cancelled);
        var previousReservations = await _db.Reservations.CountAsync(r =>
            r.Date >= previousFrom && r.Date < fromDate && r.Status != ReservationStatus.Cancelled);

        var revenueByDay = days.Select(day => new
        {
            date = day.ToString("yyyy-MM-dd"),
            amount = served.Where(o => o.Day == day).Sum(o => o.TotalAmount)
        });

        var ordersByDay = days.Select(day => new
        {
            date = day.ToString("yyyy-MM-dd"),
            count = orders.Count(o => o.Day == day)
        });

        var firstHour = orders.Count > 0 ? Math.Min(DefaultFirstHour, orders.Min(o => o.Hour)) : DefaultFirstHour;
        var lastHour = orders.Count > 0 ? Math.Max(DefaultLastHour, orders.Max(o => o.Hour)) : DefaultLastHour;
        var ordersByHour = Enumerable.Range(firstHour, lastHour - firstHour + 1).Select(hour => new
        {
            hour,
            count = orders.Count(o => o.Hour == hour)
        });

        var rangeStartUtc = _clock.StartOfDayUtc(fromDate);
        var topItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => RevenueStatuses.Contains(oi.Order!.Status))
            .Where(oi => (oi.Order!.ServedAt ?? oi.Order.CreatedAt) >= rangeStartUtc && (oi.Order.ServedAt ?? oi.Order.CreatedAt) < endUtc)
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
                status = AdminDisplay.OrderStatusLabel(o.Status)
            });

        return new JsonResult(new
        {
            revenue,
            previousRevenue,
            orders = orders.Count,
            previousOrders = previousOrders.Count,
            reservations,
            previousReservations,
            averageOrderValue = Average(revenue, served.Count),
            previousAverageOrderValue = Average(previousRevenue, previousServed.Count),
            revenueByDay,
            ordersByDay,
            ordersByHour,
            topItems,
            recentOrders
        });
    }

    private static int Average(int total, int count) =>
        count == 0 ? 0 : (int)Math.Round((double)total / count, MidpointRounding.AwayFromZero);
}
