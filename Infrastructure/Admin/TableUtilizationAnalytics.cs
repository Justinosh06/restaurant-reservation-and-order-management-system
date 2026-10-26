using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reservations;

namespace RestaurantReservation.Infrastructure.Admin;

public sealed record TableUtilization(string TableId, int Capacity, double Rate, double UsedHours, int Visits);

// Rate is null when there are no tables or the restaurant was not open at all in the range.
public sealed record TableUtilizationReport(
    int TableCount,
    double? Rate,
    double UsedHours,
    double OpenTableHours,
    int Visits,
    int AverageVisitMinutes,
    IReadOnlyList<TableUtilization> Tables);

// Table utilization (occupancy rate) = table-hours in use / table-hours the restaurant was open.
// A table is in use from a dining session's start until it ends (or until now while it is still open).
// Each date uses the opening hours that were in force that day (OpeningHoursSchedule), only time inside
// those hours counts, and today only counts up to now.
public class TableUtilizationAnalytics
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public TableUtilizationAnalytics(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TableUtilizationReport> BuildAsync(DateOnly from, DateOnly to)
    {
        var tables = await _db.Tables.AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.Capacity })
            .ToListAsync();
        var tableIds = tables.Select(t => t.Id).ToHashSet();

        var schedule = await OpeningHoursSchedule.LoadAsync(_db);
        var now = _clock.Now;
        var openWindows = new List<(DateTime Start, DateTime End)>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var day = schedule.ForDate(date);
            if (day.IsClosed)
            {
                continue;
            }

            var start = date.ToDateTime(day.Open);
            var end = date.ToDateTime(day.Close);
            if (end > now)
            {
                end = now;
            }
            if (end > start)
            {
                openWindows.Add((start, end));
            }
        }

        var startUtc = _clock.StartOfDayUtc(from);
        var endUtc = _clock.StartOfDayUtc(to.AddDays(1));
        var nowUtc = DateTime.UtcNow;
        var sessions = (await _db.Sessions.AsNoTracking()
                .Where(s => s.StartingTime < endUtc && (s.EndingTime == null || s.EndingTime > startUtc))
                .Select(s => new { s.TableId, s.StartingTime, s.EndingTime })
                .ToListAsync())
            .Where(s => tableIds.Contains(s.TableId))
            .Select(s => new
            {
                s.TableId,
                StartedInRange = s.StartingTime >= startUtc,
                Start = _clock.ToLocal(s.StartingTime),
                End = _clock.ToLocal(s.EndingTime ?? nowUtc)
            })
            .Where(s => s.End > s.Start)
            .ToList();

        var usedMinutes = tables.ToDictionary(t => t.Id, _ => 0.0);
        foreach (var session in sessions)
        {
            foreach (var window in openWindows)
            {
                var start = session.Start > window.Start ? session.Start : window.Start;
                var end = session.End < window.End ? session.End : window.End;
                if (end > start)
                {
                    usedMinutes[session.TableId] += (end - start).TotalMinutes;
                }
            }
        }

        var openMinutes = openWindows.Sum(w => (w.End - w.Start).TotalMinutes);
        var totalUsed = usedMinutes.Values.Sum();
        var openTableMinutes = openMinutes * tables.Count;
        var visits = sessions.Where(s => s.StartedInRange).ToList();

        return new TableUtilizationReport(
            TableCount: tables.Count,
            Rate: openTableMinutes > 0 ? totalUsed / openTableMinutes : null,
            UsedHours: totalUsed / 60,
            OpenTableHours: openTableMinutes / 60,
            Visits: visits.Count,
            AverageVisitMinutes: visits.Count > 0 ? (int)Math.Round(visits.Average(s => (s.End - s.Start).TotalMinutes)) : 0,
            Tables: tables
                .Select(t => new TableUtilization(
                    t.Id,
                    t.Capacity,
                    openMinutes > 0 ? usedMinutes[t.Id] / openMinutes : 0,
                    usedMinutes[t.Id] / 60,
                    visits.Count(s => s.TableId == t.Id)))
                .ToList());
    }
}
