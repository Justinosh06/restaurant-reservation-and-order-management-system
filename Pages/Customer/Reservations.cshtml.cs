using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Upcoming reservations: every booking of this customer that has not started yet, soonest first.
public class ReservationsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ReservationsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<RestaurantReservation.Models.Reservation> Reservations { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Reservations = await Upcoming(_db, User.FindFirstValue(ClaimTypes.NameIdentifier)).ToListAsync();
    }

    // Pending and confirmed bookings that have not started yet; cancelled and completed ones are history.
    public static IQueryable<RestaurantReservation.Models.Reservation> Upcoming(ApplicationDbContext db, string? customerId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = TimeOnly.FromDateTime(DateTime.Now);

        return db.Reservations
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.CustomerId == customerId
                && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed)
                && (r.Date > today || (r.Date == today && r.TimeSlot >= now)))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.TimeSlot);
    }
}
