using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;

namespace RestaurantReservation.Infrastructure.Reservations;

// The restaurant's opening hours for bookings, shared by the customer booking page and the staff
// reservation form. Hours come from Restaurant Settings; until they are saved there, the same
// defaults the Settings page shows are used.
public static class OpeningHours
{
    public static readonly TimeOnly DefaultOpen = new(11, 0);
    public static readonly TimeOnly DefaultClose = new(22, 0);

    // The last booking starts this long before closing.
    public static readonly TimeSpan LastBookingBeforeClose = TimeSpan.FromHours(1);

    public sealed record Day(bool IsClosed, TimeOnly Open, TimeOnly LastBooking);

    public static async Task<Day> ForDateAsync(ApplicationDbContext db, DateOnly date)
    {
        var hours = await db.BusinessHours.AsNoTracking()
            .FirstOrDefaultAsync(day => day.DayOfWeek == date.DayOfWeek);
        var open = hours?.OpenTime ?? DefaultOpen;
        var close = hours?.CloseTime ?? DefaultClose;
        return new Day(hours is { IsClosed: true }, open, close.Add(-LastBookingBeforeClose));
    }
}
