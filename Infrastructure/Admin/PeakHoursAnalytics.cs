using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reservations;

namespace RestaurantReservation.Infrastructure.Admin;

// AverageTables is null when there is nothing to average: Closed says the restaurant was closed in that hour on
// every such day in the range; otherwise the hour has not happened yet (later today).
public sealed record PeakHourCell(int Hour, double? AverageTables, bool Closed);

public sealed record PeakHourRow(DayOfWeek Day, IReadOnlyList<PeakHourCell> Hours);

public sealed record PeakHoursReport(int TableCount, IReadOnlyList<PeakHourRow> Rows);

// Peak hours from dining sessions: a table is in use from the session's start until it ends (or until now
// while it is still open). Only time inside the opening hours in force on that date counts (OpeningHoursSchedule),
// the same rule as table utilization, so a session left open overnight can't fill the small hours.
public class PeakHoursAnalytics
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public PeakHoursAnalytics(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // One row per weekday in the range, Monday first. Each cell is the average number of tables in use during
    // that hour, over the minutes the restaurant was open in that hour on every date in the range that falls on
    // that weekday. The columns run from the earliest opening to the latest closing hour in the range.
    public async Task<PeakHoursReport> BuildAsync(DateOnly from, DateOnly to)
    {
        var tableIds = (await _db.Tables.AsNoTracking().Select(t => t.Id).ToListAsync()).ToHashSet();
        var schedule = await OpeningHoursSchedule.LoadAsync(_db);
        var now = _clock.Now;

        var dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        var openWindows = new Dictionary<DateOnly, (DateTime Start, DateTime End)>();
        foreach (var date in dates)
        {
            var day = schedule.ForDate(date);
            if (!day.IsClosed && day.Close > day.Open)
            {
                openWindows[date] = (date.ToDateTime(day.Open), date.ToDateTime(day.Close));
            }
        }

        var startUtc = _clock.StartOfDayUtc(from);
        var endUtc = _clock.StartOfDayUtc(to.AddDays(1));
        var nowUtc = DateTime.UtcNow;
        var sessions = (await _db.Sessions.AsNoTracking()
                .Where(s => s.StartingTime < endUtc && (s.EndingTime == null || s.EndingTime > startUtc))
                .Select(s => new { s.TableId, s.StartingTime, s.EndingTime })
                .ToListAsync())
            .Where(s => tableIds.Contains(s.TableId));

        // Table-minutes in use for each (date, hour) slot, counting only time the restaurant was open.
        var usedMinutes = new Dictionary<(DateOnly Date, int Hour), double>();
        foreach (var session in sessions)
        {
            var sessionStart = _clock.ToLocal(session.StartingTime);
            var sessionEnd = _clock.ToLocal(session.EndingTime ?? nowUtc);
            foreach (var window in openWindows.Values)
            {
                var start = Later(sessionStart, window.Start);
                var end = Earlier(Earlier(sessionEnd, window.End), now);
                foreach (var slot in SplitByHour(start, end))
                {
                    usedMinutes[(slot.Date, slot.Hour)] = usedMinutes.GetValueOrDefault((slot.Date, slot.Hour)) + slot.Minutes;
                }
            }
        }

        var hours = HoursShown(openWindows.Values);
        var rows = OpeningHoursSchedule.WeekOrder
            .Where(day => dates.Any(d => d.DayOfWeek == day))
            .Select(day =>
            {
                var dayDates = dates.Where(d => d.DayOfWeek == day).ToList();
                var cells = hours.Select(hour =>
                {
                    // Minutes of this hour the restaurant was open, over all these dates: all of them for
                    // the closed-or-not test, and only those already passed for the average.
                    var openMinutes = dayDates.Sum(d => OpenMinutes(openWindows, d, hour, DateTime.MaxValue));
                    var passedMinutes = dayDates.Sum(d => OpenMinutes(openWindows, d, hour, now));
                    var used = dayDates.Sum(d => usedMinutes.GetValueOrDefault((d, hour)));
                    return new PeakHourCell(hour, passedMinutes > 0 ? used / passedMinutes : null, Closed: openMinutes == 0);
                }).ToList();
                return new PeakHourRow(day, cells);
            })
            .ToList();

        return new PeakHoursReport(tableIds.Count, rows);
    }

    // Every hour from the earliest opening to the last hour before the latest closing, e.g. 12:00-22:00
    // gives 12 to 21. With no open day in the range, the default hours.
    private static List<int> HoursShown(IEnumerable<(DateTime Start, DateTime End)> windows)
    {
        var list = windows.ToList();
        var first = list.Count > 0 ? list.Min(w => w.Start.Hour) : OpeningHours.DefaultOpen.Hour;
        var last = list.Count > 0
            ? list.Max(w => w.End.Minute == 0 ? w.End.Hour - 1 : w.End.Hour)
            : OpeningHours.DefaultClose.Hour - 1;
        return Enumerable.Range(first, Math.Max(last - first + 1, 1)).ToList();
    }

    // Minutes of the given hour on the given date that the restaurant was open, up to the given time.
    private static double OpenMinutes(Dictionary<DateOnly, (DateTime Start, DateTime End)> openWindows, DateOnly date, int hour, DateTime until)
    {
        if (!openWindows.TryGetValue(date, out var window))
        {
            return 0;
        }

        var slotStart = date.ToDateTime(new TimeOnly(hour, 0));
        var start = Later(slotStart, window.Start);
        var end = Earlier(Earlier(slotStart.AddHours(1), window.End), until);
        return end > start ? (end - start).TotalMinutes : 0;
    }

    private static IEnumerable<(DateOnly Date, int Hour, double Minutes)> SplitByHour(DateTime start, DateTime end)
    {
        var time = start;
        while (time < end)
        {
            var nextHour = new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0).AddHours(1);
            var stop = Earlier(nextHour, end);
            yield return (DateOnly.FromDateTime(time), time.Hour, (stop - time).TotalMinutes);
            time = stop;
        }
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
