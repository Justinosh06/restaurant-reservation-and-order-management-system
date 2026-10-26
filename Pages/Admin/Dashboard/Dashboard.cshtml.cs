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
    private const int MaxExportDays = 366;

    private static readonly OrderStatus[] RevenueStatuses = [OrderStatus.Served, OrderStatus.Completed];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;
    private readonly AnalyticsReportBuilder _reports;
    private readonly PeakHoursAnalytics _peakHours;

    public DashboardModel(ApplicationDbContext db, RestaurantClock clock, AnalyticsReportBuilder reports,
        PeakHoursAnalytics peakHours)
    {
        _db = db;
        _clock = clock;
        _reports = reports;
        _peakHours = peakHours;
    }

    public DateOnly Today => _clock.Today;

    // compare = "month" is sent by the "This month" button: the range is compared with the whole previous month.
    public async Task<IActionResult> OnGetStatsAsync(string? from, string? to, string? compare)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }

        var compareWholeMonth = compare == "month";
        if (compareWholeMonth && (fromDate.Day != 1 || toDate.Month != fromDate.Month || toDate.Year != fromDate.Year))
        {
            return BadRequest(new { error = "A month comparison must start on the 1st and stay within one month." });
        }

        // Usually the previous period has the same number of days and ends the day before the chosen range
        // (e.g. 8-14 Oct is compared with 1-7 Oct). For "This month" it is the whole previous month
        // (e.g. 1-14 Oct is compared with 1-30 Sep). Either way it ends the day before fromDate.
        var dayCount = toDate.DayNumber - fromDate.DayNumber + 1;
        var previousFrom = compareWholeMonth ? fromDate.AddMonths(-1) : fromDate.AddDays(-dayCount);
        var previousTo = fromDate.AddDays(-1);
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
            .Select(o => new { o.TotalAmount, Local = _clock.ToLocal(o.ServedAt) })
            .Select(o => new { o.TotalAmount, Day = DateOnly.FromDateTime(o.Local), o.Local.Hour })
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
            amount = served.Where(o => o.Day == day).Sum(o => o.TotalAmount),
            orders = served.Count(o => o.Day == day)
        });

        var reservationDays = await _db.Reservations.AsNoTracking()
            .Where(r => r.Date >= fromDate && r.Date <= toDate && r.Status != ReservationStatus.Cancelled)
            .Select(r => r.Date)
            .ToListAsync();
        var reservationsByDay = days.Select(day => new
        {
            date = day.ToString("yyyy-MM-dd"),
            count = reservationDays.Count(d => d == day)
        });

        var ordersByDay = days.Select(day => new
        {
            date = day.ToString("yyyy-MM-dd"),
            count = orders.Count(o => o.Day == day)
        });

        var busyHours = orders.Select(o => o.Hour).Concat(served.Select(o => o.Hour)).ToList();
        var firstHour = busyHours.Count > 0 ? Math.Min(DefaultFirstHour, busyHours.Min()) : DefaultFirstHour;
        var lastHour = busyHours.Count > 0 ? Math.Max(DefaultLastHour, busyHours.Max()) : DefaultLastHour;
        var ordersByHour = Enumerable.Range(firstHour, lastHour - firstHour + 1).Select(hour => new
        {
            hour,
            count = orders.Count(o => o.Hour == hour)
        });

        // Used instead of revenueByDay when the range is a single day.
        var revenueByHour = Enumerable.Range(firstHour, lastHour - firstHour + 1).Select(hour => new
        {
            hour,
            amount = served.Where(o => o.Hour == hour).Sum(o => o.TotalAmount)
        });

        var rangeStartUtc = _clock.StartOfDayUtc(fromDate);
        var topItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => RevenueStatuses.Contains(oi.Order!.Status))
            .Where(oi => (oi.Order!.ServedAt ?? oi.Order.CreatedAt) >= rangeStartUtc && (oi.Order.ServedAt ?? oi.Order.CreatedAt) < endUtc)
            .GroupBy(oi => oi.ItemName)
            .Select(g => new { name = g.Key, units = g.Sum(oi => oi.Quantity) })
            .OrderByDescending(x => x.units)
            .Take(5)
            .ToListAsync();

        var recentOrders = (await _db.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new
                {
                    o.Id,
                    o.TotalAmount,
                    o.Status,
                    o.CreatedAt,
                    o.Session!.TableId,
                    CustomerEmail = o.Customer!.Email,
                    ItemCount = _db.OrderItems.Where(oi => oi.OrderId == o.Id).Sum(oi => oi.Quantity)
                })
                .ToListAsync())
            .Select(o => new
            {
                id = AdminDisplay.ShortId(o.Id),
                table = o.TableId,
                customer = o.CustomerEmail,
                itemCount = o.ItemCount,
                createdAt = o.CreatedAt,
                total = o.TotalAmount,
                status = AdminDisplay.OrderStatusLabel(o.Status)
            });

        var peakHours = await _peakHours.BuildAsync(fromDate, toDate);

        return new JsonResult(new
        {
            previousFrom = previousFrom.ToString("yyyy-MM-dd"),
            previousTo = previousTo.ToString("yyyy-MM-dd"),
            compareWholeMonth,
            revenue,
            previousRevenue,
            orders = orders.Count,
            previousOrders = previousOrders.Count,
            reservations,
            previousReservations,
            averageOrderValue = Average(revenue, served.Count),
            previousAverageOrderValue = Average(previousRevenue, previousServed.Count),
            revenueByDay,
            revenueByHour,
            reservationsByDay,
            ordersByDay,
            ordersByHour,
            topItems,
            recentOrders,
            peakHours = new
            {
                tableCount = peakHours.TableCount,
                rows = peakHours.Rows.Select(row => new
                {
                    day = (int)row.Day,
                    hours = row.Hours.Select(cell => new
                    {
                        hour = cell.Hour,
                        tables = cell.AverageTables.HasValue ? Math.Round(cell.AverageTables.Value, 1) : (double?)null
                    })
                })
            }
        });
    }

    // Downloads the business analytics report for the chosen dates: format "pdf" for a printable report,
    // "csv" for the same figures plus every order, to open in a spreadsheet.
    public async Task<IActionResult> OnGetExportAsync(string? from, string? to, string? format)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }
        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxExportDays)
        {
            return BadRequest(new { error = "An export can cover up to one year. Please choose a shorter date range." });
        }
        if (format is not ("pdf" or "csv"))
        {
            return BadRequest(new { error = "Please choose PDF or CSV." });
        }

        var report = await _reports.BuildAsync(fromDate, toDate);
        var fileName = fromDate == toDate
            ? $"analytics-{fromDate:yyyy-MM-dd}.{format}"
            : $"analytics-{fromDate:yyyy-MM-dd}-to-{toDate:yyyy-MM-dd}.{format}";
        return format == "pdf"
            ? File(AnalyticsReportPdf.Write(report), "application/pdf", fileName)
            : File(AnalyticsReportCsv.Write(report), "text/csv", fileName);
    }

    private static int Average(int total, int count) =>
        count == 0 ? 0 : (int)Math.Round((double)total / count, MidpointRounding.AwayFromZero);
}
