using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Reservations;

// The opening hours that were in force on any date, from OpeningHoursHistory. Dates before the history starts
// (and every date, until Restaurant Settings is first saved) use the current hours.
public sealed class OpeningHoursSchedule
{
    // The week as the admin pages list it, Monday first.
    public static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public sealed record Day(bool IsClosed, TimeOnly Open, TimeOnly Close);

    public sealed record WeekdayHours(DayOfWeek DayOfWeek, bool IsClosed, TimeOnly? Open, TimeOnly? Close);

    private readonly List<OpeningHoursHistory> _history;
    private readonly List<BusinessHours> _current;

    private OpeningHoursSchedule(List<OpeningHoursHistory> history, List<BusinessHours> current)
    {
        _history = history;
        _current = current;
    }

    public static async Task<OpeningHoursSchedule> LoadAsync(ApplicationDbContext db) =>
        new(await db.OpeningHoursHistory.AsNoTracking().OrderByDescending(h => h.EffectiveFrom).ToListAsync(),
            await db.BusinessHours.AsNoTracking().ToListAsync());

    public Day ForDate(DateOnly date)
    {
        var recorded = _history.FirstOrDefault(h => h.EffectiveFrom <= date && h.DayOfWeek == date.DayOfWeek);
        if (recorded is not null)
        {
            return new Day(recorded.IsClosed, recorded.OpenTime ?? OpeningHours.DefaultOpen, recorded.CloseTime ?? OpeningHours.DefaultClose);
        }

        var current = _current.FirstOrDefault(h => h.DayOfWeek == date.DayOfWeek);
        return new Day(current is { IsClosed: true }, current?.OpenTime ?? OpeningHours.DefaultOpen, current?.CloseTime ?? OpeningHours.DefaultClose);
    }

    // Called by Restaurant Settings when it saves the weekly hours. The first time, the hours from before the
    // change are kept as the schedule for all earlier dates; after that, a new copy starting today is added
    // whenever the hours actually change (a second change on the same day replaces the first).
    public static async Task RecordAsync(
        ApplicationDbContext db, IReadOnlyList<WeekdayHours> previousHours, IReadOnlyList<WeekdayHours> newHours, DateOnly today)
    {
        var history = await db.OpeningHoursHistory.ToListAsync();
        if (history.Count == 0)
        {
            var baseline = Rows(previousHours, DateOnly.MinValue);
            db.OpeningHoursHistory.AddRange(baseline);
            history.AddRange(baseline);
        }

        var inForceToday = history
            .Where(h => h.EffectiveFrom <= today)
            .GroupBy(h => h.DayOfWeek)
            .Select(g => g.OrderByDescending(h => h.EffectiveFrom).First())
            .ToList();
        var unchanged = newHours.All(day => inForceToday.Any(h =>
            h.DayOfWeek == day.DayOfWeek && h.IsClosed == day.IsClosed && h.OpenTime == day.Open && h.CloseTime == day.Close));
        if (unchanged)
        {
            return;
        }

        db.OpeningHoursHistory.RemoveRange(history.Where(h => h.EffectiveFrom == today));
        db.OpeningHoursHistory.AddRange(Rows(newHours, today));
    }

    private static List<OpeningHoursHistory> Rows(IEnumerable<WeekdayHours> hours, DateOnly effectiveFrom) =>
        hours.Select(day => new OpeningHoursHistory
        {
            Id = Guid.NewGuid().ToString("N"),
            EffectiveFrom = effectiveFrom,
            DayOfWeek = day.DayOfWeek,
            IsClosed = day.IsClosed,
            OpenTime = day.IsClosed ? null : day.Open,
            CloseTime = day.IsClosed ? null : day.Close
        }).ToList();
}
