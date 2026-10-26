using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reservations;

namespace RestaurantReservation.Infrastructure.Admin;

// AverageTables is null for an hour that has not happened yet (later today).
public sealed record PeakHourCell(int Hour, double? AverageTables);

public sealed record PeakHourRow(DayOfWeek Day, IReadOnlyList<PeakHourCell> Hours);

public sealed record PeakHoursReport(int TableCount, IReadOnlyList<PeakHourRow> Rows);

// Peak hours from dining sessions: a table is in use from the session's start until it ends (or until now
// while it is still open). Opening hours are not used to cut sessions off, because they can change over time
// and only the current ones are stored; a session can only happen while the restaurant is open, so the
// sessions themselves show the hours it really was open.
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
    // that hour, across every date in the range that falls on that weekday. The hours shown are the current
    // opening hours plus any other hour that had a session in the range (e.g. under earlier opening hours).
    public async Task<PeakHoursReport> BuildAsync(DateOnly from, DateOnly to)
    {
        var tableIds = (await _db.Tables.AsNoTracking().Select(t => t.Id).ToListAsync()).ToHashSet();

        var now = _clock.Now;
        var rangeStart = from.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (rangeEnd > now)
        {
            rangeEnd = now;
        }

        var startUtc = _clock.StartOfDayUtc(from);
        var endUtc = _clock.StartOfDayUtc(to.AddDays(1));
        var nowUtc = DateTime.UtcNow;
        var sessions = (await _db.Sessions.AsNoTracking()
                .Where(s => s.StartingTime < endUtc && (s.EndingTime == null || s.EndingTime > startUtc))
                .Select(s => new { s.TableId, s.StartingTime, s.EndingTime })
                .ToListAsync())
            .Where(s => tableIds.Contains(s.TableId));

        // Table-minutes in use for each (date, hour) slot.
        var usedMinutes = new Dictionary<(DateOnly Date, int Hour), double>();
        foreach (var session in sessions)
        {
            var start = Later(_clock.ToLocal(session.StartingTime), rangeStart);
            var end = Earlier(_clock.ToLocal(session.EndingTime ?? nowUtc), rangeEnd);
            foreach (var slot in SplitByHour(start, end))
            {
                usedMinutes[(slot.Date, slot.Hour)] = usedMinutes.GetValueOrDefault((slot.Date, slot.Hour)) + slot.Minutes;
            }
        }

        var hours = (await CurrentOpeningHoursAsync())
            .Concat(usedMinutes.Keys.Select(k => k.Hour))
            .Distinct()
            .Order()
            .ToList();
        hours = Enumerable.Range(hours.Min(), hours.Max() - hours.Min() + 1).ToList();

        var dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        var rows = OpeningHoursSchedule.WeekOrder
            .Where(day => dates.Any(d => d.DayOfWeek == day))
            .Select(day =>
            {
                var dayDates = dates.Where(d => d.DayOfWeek == day).ToList();
                var cells = hours.Select(hour =>
                {
                    // Minutes of this hour that have already happened, over all these dates (less than 60 for
                    // the current hour today, none for later hours today).
                    var passedMinutes = dayDates.Sum(d => MinutesPassed(d, hour, now));
                    var used = dayDates.Sum(d => usedMinutes.GetValueOrDefault((d, hour)));
                    return new PeakHourCell(hour, passedMinutes > 0 ? used / passedMinutes : null);
                }).ToList();
                return new PeakHourRow(day, cells);
            })
            .ToList();

        return new PeakHoursReport(tableIds.Count, rows);
    }

    // Each hour the restaurant is open in the current weekly hours, e.g. 12:00-22:00 gives 12 to 21.
    private async Task<List<int>> CurrentOpeningHoursAsync()
    {
        var businessHours = await _db.BusinessHours.AsNoTracking().ToListAsync();
        var hours = new List<int>();
        foreach (var day in OpeningHoursSchedule.WeekOrder)
        {
            var dayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == day);
            if (dayHours is { IsClosed: true })
            {
                continue;
            }

            var open = dayHours?.OpenTime ?? OpeningHours.DefaultOpen;
            var close = dayHours?.CloseTime ?? OpeningHours.DefaultClose;
            var lastHour = close.Minute == 0 ? close.Hour - 1 : close.Hour;
            for (var hour = open.Hour; hour <= lastHour; hour++)
            {
                hours.Add(hour);
            }
        }

        return hours.Count > 0 ? hours : [OpeningHours.DefaultOpen.Hour];
    }

    private static double MinutesPassed(DateOnly date, int hour, DateTime now)
    {
        var slotStart = date.ToDateTime(new TimeOnly(hour, 0));
        return Math.Clamp((now - slotStart).TotalMinutes, 0, 60);
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
