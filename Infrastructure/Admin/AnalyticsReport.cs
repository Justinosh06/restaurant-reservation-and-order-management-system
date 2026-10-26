using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Infrastructure.Admin;

// The business analytics report the dashboard exports as CSV or PDF. Amounts are in cents.
// It counts orders the same way as the dashboard: revenue comes from served and completed orders on the day
// they were served, and "orders placed" are all orders made that day except cancelled ones.
public static class RevenueRules
{
    // The order statuses that count as revenue, on the dashboard and in the export.
    public static readonly OrderStatus[] Statuses = [OrderStatus.Served, OrderStatus.Completed];
}

public record AnalyticsReport(
    string RestaurantName,
    string RestaurantAddress,
    CurrencyType Currency,
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    DateTime GeneratedAt,
    AnalyticsPeriod Current,
    AnalyticsPeriod Previous,
    IReadOnlyList<AnalyticsDay> Days,
    IReadOnlyList<AnalyticsHour> Hours,
    IReadOnlyList<AnalyticsItem> Items,
    IReadOnlyList<AnalyticsCategory> Categories,
    IReadOnlyList<AnalyticsPayment> Payments,
    int PromoDiscounts,
    int DepositCredits,
    IReadOnlyList<AnalyticsReservationStatus> ReservationStatuses,
    IReadOnlyList<AnalyticsOrder> Orders,
    TableUtilizationReport Utilization,
    TableUtilizationReport PreviousUtilization,
    IReadOnlyList<ReservationSlot> ReservationSlots,
    PeakHoursReport PeakHours)
{
    public int DayCount => To.DayNumber - From.DayNumber + 1;

    public string Money(int cents) => Currency.FormatCents(cents);

    // e.g. "+12.5%", "-3.0%", "New" when there was nothing before, or "-" when both are zero.
    public static string Change(decimal current, decimal previous) =>
        previous == 0
            ? current == 0 ? "-" : "New"
            : $"{(current - previous) / previous * 100:+0.0;-0.0;0.0}%";

    // For values that are already percentages (e.g. a cancellation rate): the difference in points, e.g. "+4.5 pts".
    public static string ChangeInPoints(decimal current, decimal previous) =>
        $"{current - previous:+0.0;-0.0;0.0} pts";
}

public record AnalyticsPeriod(
    int Revenue,
    int OrdersPlaced,
    int OrdersServed,
    int OrdersCancelled,
    int ItemsSold,
    int Reservations,
    int Guests,
    int ReservationsCancelled,
    int NoShows,
    double? AverageLeadDays,
    double? TableUtilization)
{
    // Table utilization as a percentage with one decimal, or 0 when there were no tables or opening hours.
    public decimal UtilizationPercent => TableUtilization is null
        ? 0
        : Math.Round((decimal)TableUtilization.Value * 100, 1, MidpointRounding.AwayFromZero);

    public int AverageOrderValue => OrdersServed == 0
        ? 0
        : (int)Math.Round((decimal)Revenue / OrdersServed, MidpointRounding.AwayFromZero);

    // Cancelled bookings (no-shows included) out of all bookings for those dates, as a percentage.
    public decimal CancellationRate => Reservations + ReservationsCancelled == 0
        ? 0
        : Math.Round(100m * ReservationsCancelled / (Reservations + ReservationsCancelled), 1);
}

public record AnalyticsDay(DateOnly Date, int OrdersPlaced, int OrdersServed, int Revenue, int Reservations, int Guests)
{
    public int AverageOrderValue => OrdersServed == 0
        ? 0
        : (int)Math.Round((decimal)Revenue / OrdersServed, MidpointRounding.AwayFromZero);
}

public record AnalyticsHour(int Hour, int OrdersPlaced, int Revenue)
{
    public string Label => $"{new DateTime(2000, 1, 1, Hour, 0, 0):h tt} - {new DateTime(2000, 1, 1, Hour, 0, 0).AddHours(1):h tt}";
}

public record AnalyticsItem(string Name, string Category, int Units, int Revenue, decimal ShareOfRevenue);

public record AnalyticsCategory(string Category, int Units, int Revenue, decimal ShareOfRevenue);

public record AnalyticsPayment(string Method, int Payments, int Amount);

public record AnalyticsReservationStatus(string Status, int Reservations, int Guests);

public record AnalyticsOrder(
    string Id,
    DateTime PlacedAt,
    DateTime? ServedAt,
    string? Table,
    string? Customer,
    int Items,
    string Status,
    int Total);

public class AnalyticsReportBuilder
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;
    private readonly TableUtilizationAnalytics _utilization;
    private readonly ReservationAnalytics _reservations;
    private readonly PeakHoursAnalytics _peakHours;

    public AnalyticsReportBuilder(ApplicationDbContext db, RestaurantClock clock, TableUtilizationAnalytics utilization,
        ReservationAnalytics reservations, PeakHoursAnalytics peakHours)
    {
        _db = db;
        _clock = clock;
        _utilization = utilization;
        _reservations = reservations;
        _peakHours = peakHours;
    }

    // Builds the report for from..to (whole days, restaurant time), compared with the same number of days before it.
    public async Task<AnalyticsReport> BuildAsync(DateOnly from, DateOnly to)
    {
        var dayCount = to.DayNumber - from.DayNumber + 1;
        var previousFrom = from.AddDays(-dayCount);
        var previousTo = from.AddDays(-1);
        var startUtc = _clock.StartOfDayUtc(from);
        var endUtc = _clock.StartOfDayUtc(to.AddDays(1));
        var previousStartUtc = _clock.StartOfDayUtc(previousFrom);

        var settings = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();

        // Every order touching either period, placed or served in it. Small enough to work through in memory.
        var orders = (await _db.Orders.AsNoTracking()
                .Where(o => (o.CreatedAt >= previousStartUtc && o.CreatedAt < endUtc) ||
                            (o.ServedAt != null && o.ServedAt >= previousStartUtc && o.ServedAt < endUtc))
                .Select(o => new
                {
                    o.Id,
                    o.Status,
                    o.TotalAmount,
                    o.CreatedAt,
                    o.ServedAt,
                    Table = o.Session!.TableId,
                    Customer = o.Customer!.Email,
                    Items = o.OrderItems.Sum(oi => oi.Quantity)
                })
                .ToListAsync())
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.TotalAmount,
                o.Table,
                o.Customer,
                o.Items,
                PlacedLocal = _clock.ToLocal(o.CreatedAt),
                ServedLocal = o.ServedAt is null ? (DateTime?)null : _clock.ToLocal(o.ServedAt.Value),
                // Revenue counts on the day the order was served (or placed, for orders saved before ServedAt existed).
                RevenueLocal = _clock.ToLocal(o.ServedAt ?? o.CreatedAt),
                IsRevenue = RevenueRules.Statuses.Contains(o.Status)
            })
            .ToList();

        // Bookings are counted the same way as on the dashboard: a no-show counts as cancelled (ReservationAnalytics).
        var now = _clock.Now;
        var reservations = (await _db.Reservations.AsNoTracking()
                .Where(r => r.Date >= previousFrom && r.Date <= to)
                .Select(r => new { r.Date, r.TimeSlot, r.Status, r.Pax })
                .ToListAsync())
            .Select(r => new { r.Date, r.Status, r.Pax, Outcome = ReservationAnalytics.Outcome(r.Status, r.Date, r.TimeSlot, now) })
            .ToList();

        var bookings = await _reservations.BuildAsync(from, to);
        var previousBookings = await _reservations.BuildAsync(previousFrom, previousTo);
        var utilization = await _utilization.BuildAsync(from, to);
        var previousUtilization = await _utilization.BuildAsync(previousFrom, previousTo);

        AnalyticsPeriod Period(DateOnly first, DateOnly last, ReservationTotals bookingTotals, TableUtilizationReport tables)
        {
            bool In(DateTime local) => DateOnly.FromDateTime(local) is var day && day >= first && day <= last;
            var placed = orders.Where(o => In(o.PlacedLocal)).ToList();
            var served = orders.Where(o => o.IsRevenue && In(o.RevenueLocal)).ToList();
            return new AnalyticsPeriod(
                Revenue: served.Sum(o => o.TotalAmount),
                OrdersPlaced: placed.Count(o => o.Status != OrderStatus.Cancelled),
                OrdersServed: served.Count,
                OrdersCancelled: placed.Count(o => o.Status == OrderStatus.Cancelled),
                ItemsSold: served.Sum(o => o.Items),
                Reservations: bookingTotals.Bookings,
                Guests: bookingTotals.Guests,
                ReservationsCancelled: bookingTotals.Cancelled,
                NoShows: bookingTotals.NoShows,
                AverageLeadDays: bookingTotals.AverageLeadDays,
                TableUtilization: tables.Rate);
        }

        bool InRange(DateTime local) => DateOnly.FromDateTime(local) is var day && day >= from && day <= to;
        var placedInRange = orders.Where(o => InRange(o.PlacedLocal) && o.Status != OrderStatus.Cancelled).ToList();
        // An order placed late on the last day may be served after it; its revenue belongs to the next period.
        var servedInRange = orders.Where(o => o.IsRevenue && InRange(o.RevenueLocal)).ToList();
        var reservationsInRange = reservations.Where(r => r.Date >= from && r.Outcome == ReservationOutcome.Kept).ToList();

        var days = Enumerable.Range(0, dayCount).Select(i => from.AddDays(i)).Select(day => new AnalyticsDay(
            day,
            OrdersPlaced: placedInRange.Count(o => DateOnly.FromDateTime(o.PlacedLocal) == day),
            OrdersServed: servedInRange.Count(o => DateOnly.FromDateTime(o.RevenueLocal) == day),
            Revenue: servedInRange.Where(o => DateOnly.FromDateTime(o.RevenueLocal) == day).Sum(o => o.TotalAmount),
            Reservations: reservationsInRange.Count(r => r.Date == day),
            Guests: reservationsInRange.Where(r => r.Date == day).Sum(r => r.Pax))).ToList();

        // Only the hours that had any orders, so a quiet morning doesn't fill the report with zero rows.
        var hours = placedInRange.Select(o => o.PlacedLocal.Hour)
            .Concat(servedInRange.Select(o => o.RevenueLocal.Hour))
            .Distinct()
            .Order()
            .Select(hour => new AnalyticsHour(
                hour,
                OrdersPlaced: placedInRange.Count(o => o.PlacedLocal.Hour == hour),
                Revenue: servedInRange.Where(o => o.RevenueLocal.Hour == hour).Sum(o => o.TotalAmount)))
            .ToList();

        var servedIds = servedInRange.Select(o => o.Id).ToList();
        var soldLines = await _db.OrderItems.AsNoTracking()
            .Where(oi => servedIds.Contains(oi.OrderId))
            .Select(oi => new { oi.ItemName, oi.Quantity, oi.UnitPrice, Type = (MenuItemType?)oi.MenuItem!.MenuItemType })
            .ToListAsync();
        var itemRevenue = soldLines.Sum(l => l.Quantity * l.UnitPrice);
        decimal Share(int amount) => itemRevenue == 0 ? 0 : Math.Round(100m * amount / itemRevenue, 1);

        var items = soldLines
            .GroupBy(l => l.ItemName)
            .Select(g => new
            {
                Name = g.Key,
                // A dish deleted from the menu keeps its name on past orders but no longer has a category.
                Category = g.Select(l => l.Type).FirstOrDefault(t => t is not null)?.ToString() ?? "Removed from menu",
                Units = g.Sum(l => l.Quantity),
                Revenue = g.Sum(l => l.Quantity * l.UnitPrice)
            })
            .OrderByDescending(i => i.Revenue)
            .ThenByDescending(i => i.Units)
            .ThenBy(i => i.Name)
            .Select(i => new AnalyticsItem(i.Name, i.Category, i.Units, i.Revenue, Share(i.Revenue)))
            .ToList();

        var categories = items
            .GroupBy(i => i.Category)
            .Select(g => new { Category = g.Key, Units = g.Sum(i => i.Units), Revenue = g.Sum(i => i.Revenue) })
            .OrderByDescending(c => c.Revenue)
            .Select(c => new AnalyticsCategory(c.Category, c.Units, c.Revenue, Share(c.Revenue)))
            .ToList();

        // Money actually collected in the range, by how it was paid, plus what promo codes and deposits covered.
        var transactions = await _db.Transactions.AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Succeeded && t.CreatedAt >= startUtc && t.CreatedAt < endUtc)
            .Select(t => new { t.PaymentMethod, t.Amount, t.MetadataJson })
            .ToListAsync();
        var payments = transactions
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.PaymentMethod)
            .Select(g => new AnalyticsPayment(PaymentMethodLabel(g.Key), g.Count(), g.Sum(t => t.Amount)))
            .OrderByDescending(p => p.Amount)
            .ToList();

        // A booking not shown up for gets its own row, so the cancelled total can be checked against the summary.
        var reservationStatuses = reservations
            .Where(r => r.Date >= from)
            .GroupBy(r => r.Outcome == ReservationOutcome.NoShow ? "No-show (counted as cancelled)"
                : r.Status == ReservationStatus.CheckedIn ? "Checked in" : r.Status.ToString())
            .OrderBy(g => g.Key)
            .Select(g => new AnalyticsReservationStatus(g.Key, g.Count(), g.Sum(r => r.Pax)))
            .ToList();

        var orderList = orders
            .Where(o => InRange(o.PlacedLocal))
            .OrderBy(o => o.PlacedLocal)
            .Select(o => new AnalyticsOrder(
                AdminDisplay.ShortId(o.Id), o.PlacedLocal, o.ServedLocal, o.Table, o.Customer, o.Items,
                AdminDisplay.OrderStatusLabel(o.Status), o.TotalAmount))
            .ToList();

        return new AnalyticsReport(
            RestaurantName: string.IsNullOrWhiteSpace(settings?.Name) ? "Restaurant" : settings.Name,
            RestaurantAddress: settings?.Address ?? string.Empty,
            Currency: settings?.DefaultCurrency ?? CurrencyType.MYR,
            From: from,
            To: to,
            PreviousFrom: previousFrom,
            PreviousTo: previousTo,
            GeneratedAt: _clock.Now,
            Current: Period(from, to, bookings.Totals, utilization),
            Previous: Period(previousFrom, previousTo, previousBookings.Totals, previousUtilization),
            Days: days,
            Hours: hours,
            Items: items,
            Categories: categories,
            Payments: payments,
            PromoDiscounts: transactions.Sum(t => ReadCents(t.MetadataJson, "promoDiscount")),
            DepositCredits: transactions.Sum(t => ReadCents(t.MetadataJson, "reservationDepositCredit")),
            ReservationStatuses: reservationStatuses,
            Orders: orderList,
            Utilization: utilization,
            PreviousUtilization: previousUtilization,
            ReservationSlots: bookings.Slots,
            PeakHours: await _peakHours.BuildAsync(from, to));
    }

    private static string PaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Fpx => "FPX online banking",
        PaymentMethod.EWallet => "E-wallet",
        PaymentMethod.GrabPay => "GrabPay",
        PaymentMethod.Other => "Other",
        _ => method.ToString()
    };

    // A whole-cents number saved in a transaction's metadata (see the checkout and payment pages); 0 when absent.
    private static int ReadCents(string? metadataJson, string property)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return 0;
        }

        try
        {
            using var metadata = JsonDocument.Parse(metadataJson);
            return metadata.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var cents)
                ? cents
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
