using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;

namespace RestaurantReservation.Infrastructure.Reservations;

public sealed class RestaurantOrderingHours
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public RestaurantOrderingHours(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> IsOrderingClosedAsync()
    {
        var now = _clock.Now;
        var hours = await _db.BusinessHours.AsNoTracking()
            .FirstOrDefaultAsync(day => day.DayOfWeek == now.DayOfWeek);

        return hours?.IsClosed == true ||
            (hours?.CloseTime ?? OpeningHours.DefaultClose) <= TimeOnly.FromDateTime(now);
    }
}
