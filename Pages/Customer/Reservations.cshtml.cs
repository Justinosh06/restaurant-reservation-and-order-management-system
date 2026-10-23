using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// The customer's reservations in three tabs:
// - Upcoming: bookings whose day has not passed yet, soonest first. A booking cancelled by the customer or by staff
//   stays here (marked Cancelled) until its day is over.
// - Done: completed bookings, and any other booking whose day has passed; newest first.
// - Cancelled: every cancelled booking, newest first.
// Upcoming bookings can be cancelled here up to 24 hours before (same rule as the booking page).
public class ReservationsModel(ApplicationDbContext db, IPaymentService payments, ILogger<ReservationsModel> logger) : PageModel
{
    public const string UpcomingTab = "upcoming";
    public const string DoneTab = "done";
    public const string CancelledTab = "cancelled";

    // ?tab=done or ?tab=cancelled; anything else shows Upcoming.
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public string ActiveTab => Tab is DoneTab or CancelledTab ? Tab : UpcomingTab;

    public IReadOnlyList<RestaurantReservation.Models.Reservation> Upcoming { get; private set; } = [];
    public IReadOnlyList<RestaurantReservation.Models.Reservation> Done { get; private set; } = [];
    public IReadOnlyList<RestaurantReservation.Models.Reservation> Cancelled { get; private set; } = [];

    public IReadOnlyList<RestaurantReservation.Models.Reservation> Shown => ActiveTab switch
    {
        DoneTab => Done,
        CancelledTab => Cancelled,
        _ => Upcoming
    };

    [TempData]
    public string? StatusMessage { get; set; }

    public bool CanCancel(RestaurantReservation.Models.Reservation reservation) =>
        ReservationCancellation.CanCancel(reservation, DateTime.Now);

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var all = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.ReservationTables)
            .Where(r => r.CustomerId == User.FindFirstValue(ClaimTypes.NameIdentifier))
            .ToListAsync();

        Upcoming = all
            .Where(r => r.Date >= today && r.Status != ReservationStatus.Completed)
            .OrderBy(r => r.Date).ThenBy(r => r.TimeSlot)
            .ToList();
        Done = all
            .Where(r => r.Status == ReservationStatus.Completed || (r.Date < today && r.Status != ReservationStatus.Cancelled))
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.TimeSlot)
            .ToList();
        Cancelled = all
            .Where(r => r.Status == ReservationStatus.Cancelled)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.TimeSlot)
            .ToList();
    }

    // Cancel button on an upcoming booking.
    public async Task<IActionResult> OnPostCancelAsync(string? reservationId)
    {
        StatusMessage = await ReservationCancellation.CancelAsync(
            db, payments, logger, User.FindFirstValue(ClaimTypes.NameIdentifier), reservationId);
        return RedirectToPage(new { tab = UpcomingTab });
    }
}
