using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

namespace RestaurantReservation.Infrastructure.Admin;

public enum ReservationOutcome
{
    Kept,
    Cancelled,
    NoShow
}

public sealed record ReservationTotals(int Bookings, int Guests, int Cancelled, int NoShows, double? AverageLeadDays)
{
    // Cancelled bookings (no-shows included) out of all bookings for those dates, as a percentage.
    public double CancellationRate => Bookings + Cancelled == 0
        ? 0
        : Math.Round(100.0 * Cancelled / (Bookings + Cancelled), 1, MidpointRounding.AwayFromZero);
}

public sealed record ReservationDay(DateOnly Date, int Bookings, int Guests);

public sealed record ReservationSlot(TimeOnly Time, int Bookings, int Guests);

public sealed record ReservationReport(ReservationTotals Totals, IReadOnlyList<ReservationDay> Days, IReadOnlyList<ReservationSlot> Slots);

// Reservation trends for the dining dates in a range. A booking still Pending or Confirmed after its table slot
// has ended was never checked in, so the guests didn't come: it counts as cancelled (and is also counted as a
// no-show). Cancelled and no-show bookings are left out of the bookings, guests, days and time slots.
public class ReservationAnalytics
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public ReservationAnalytics(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public static ReservationOutcome Outcome(ReservationStatus status, DateOnly date, TimeOnly timeSlot, DateTime now) =>
        status == ReservationStatus.Cancelled
            ? ReservationOutcome.Cancelled
            : status is ReservationStatus.Pending or ReservationStatus.Confirmed
              && date.ToDateTime(timeSlot) + ReservationSummary.SlotLength <= now
                ? ReservationOutcome.NoShow
                : ReservationOutcome.Kept;

    public async Task<ReservationReport> BuildAsync(DateOnly from, DateOnly to)
    {
        var now = _clock.Now;
        var reservations = (await _db.Reservations.AsNoTracking()
                .Where(r => r.Date >= from && r.Date <= to)
                .Select(r => new { r.Date, r.TimeSlot, r.Pax, r.Status, r.CreatedAt })
                .ToListAsync())
            .Select(r => new { r.Date, r.TimeSlot, r.Pax, r.CreatedAt, Outcome = Outcome(r.Status, r.Date, r.TimeSlot, now) })
            .ToList();

        var kept = reservations.Where(r => r.Outcome == ReservationOutcome.Kept).ToList();
        var noShows = reservations.Count(r => r.Outcome == ReservationOutcome.NoShow);

        // How far ahead the booking was made: from when it was created to the reserved time, in days.
        var leadDays = kept
            .Select(r => Math.Max(0, (r.Date.ToDateTime(r.TimeSlot) - _clock.ToLocal(r.CreatedAt)).TotalDays))
            .ToList();

        var totals = new ReservationTotals(
            Bookings: kept.Count,
            Guests: kept.Sum(r => r.Pax),
            Cancelled: reservations.Count - kept.Count,
            NoShows: noShows,
            AverageLeadDays: leadDays.Count > 0 ? Math.Round(leadDays.Average(), 1) : null);

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Select(day => new ReservationDay(day, kept.Count(r => r.Date == day), kept.Where(r => r.Date == day).Sum(r => r.Pax)))
            .ToList();

        var slots = kept
            .GroupBy(r => r.TimeSlot)
            .OrderBy(g => g.Key)
            .Select(g => new ReservationSlot(g.Key, g.Count(), g.Sum(r => r.Pax)))
            .ToList();

        return new ReservationReport(totals, days, slots);
    }
}
