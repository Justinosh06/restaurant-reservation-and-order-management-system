using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Customers pick a party size, a date and a time. Tables are not chosen here:
// the booking starts as Pending and staff seat it on Table Management.
public class ReserveModel(ApplicationDbContext db) : PageModel
{
    // Used when the restaurant has not set its business hours yet.
    private static readonly TimeOnly DefaultOpen = new(8, 0);
    private static readonly TimeOnly DefaultClose = new(22, 0);

    private static readonly TimeSpan SlotStep = TimeSpan.FromHours(1);

    // The last booking starts this long before closing.
    private static readonly TimeSpan LastBookingBeforeClose = TimeSpan.FromHours(1);

    public const int MaxPartySize = 20;

    // Bookings open this many days ahead, so the kitchen has time to prepare.
    public const int MinDaysAhead = 2;
    public const int DaysAhead = 60;

    [BindProperty]
    public BookingInput Input { get; set; } = new();

    // Weekdays (0 = Sunday) the restaurant is closed, for greying out the calendar.
    public List<int> ClosedDays { get; private set; } = [];

    public List<ReservationSummary> Upcoming { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public DateOnly FirstDay => Today.AddDays(MinDaysAhead);

    public DateOnly LastDay => Today.AddDays(DaysAhead);

    // Customers can cancel until the day before their booking, not on the day itself.
    public bool CanCancel(ReservationSummary booking) =>
        DateOnly.FromDateTime(booking.ReservationAt) > Today;

    public async Task OnGetAsync()
    {
        await LoadPageAsync();
    }

    // GET /Customer/Reserve?handler=Slots&date=2026-10-20&guests=4 - the times on that day and whether each has room.
    public async Task<IActionResult> OnGetSlotsAsync(DateOnly date, int guests)
    {
        if (date < FirstDay || date > LastDay || guests < 1 || guests > MaxPartySize)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var slots = await GetSlotsAsync(date, guests);
        return new JsonResult(slots.Select(slot => new
        {
            time = slot.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
            available = slot.Available
        }));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.Date < FirstDay || Input.Date > LastDay)
        {
            ModelState.AddModelError("Input.Date",
                $"Choose a date from {FirstDay.ToString("d MMM", CultureInfo.CurrentCulture)} to {LastDay.ToString("d MMM", CultureInfo.CurrentCulture)}. "
                + $"Bookings must be made at least {MinDaysAhead} days ahead.");
        }
        else if (ModelState.IsValid)
        {
            var slot = (await GetSlotsAsync(Input.Date, Input.Guests))
                .FirstOrDefault(item => item.Time == Input.Time);
            if (slot is null)
            {
                ModelState.AddModelError("Input.Time", "Choose one of the listed times.");
            }
            else if (!slot.Available)
            {
                ModelState.AddModelError("Input.Time", $"There is no longer room for {Input.Guests} at that time. Please choose another.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        var reservation = new Reservation
        {
            Date = Input.Date,
            TimeSlot = Input.Time,
            Pax = Input.Guests,
            Status = ReservationStatus.Pending,
            CustomerId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim()
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        var at = Input.Date.ToDateTime(Input.Time);
        StatusMessage = $"Booked for {Input.Guests} on {at.ToString("dddd d MMMM 'at' h:mm tt", CultureInfo.CurrentCulture)}. "
            + "We'll confirm your table soon.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(string? reservationId)
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reservation = reservationId is null ? null : await db.Reservations
            .Include(item => item.Customer)
            .Include(item => item.ReservationTables)
            .FirstOrDefaultAsync(item => item.Id == reservationId && item.CustomerId == customerId);
        if (reservation is null || reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
        {
            StatusMessage = "That booking can no longer be cancelled.";
            return RedirectToPage();
        }

        var booking = ReservationSummary.From(reservation);
        var at = booking.ReservationAt.ToString("dddd d MMMM 'at' h:mm tt", CultureInfo.CurrentCulture);
        if (!CanCancel(booking))
        {
            StatusMessage = $"Your booking on {at} is today, so it can't be cancelled online. Please call the restaurant.";
            return RedirectToPage();
        }

        // Kept as Cancelled (not deleted) so staff can still see it; cancelled bookings don't hold tables.
        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();
        StatusMessage = $"Your booking on {at} is cancelled.";
        return RedirectToPage();
    }

    private async Task LoadPageAsync()
    {
        ClosedDays = (await LoadHoursAsync())
            .Where(day => day.IsClosed)
            .Select(day => (int)day.DayOfWeek)
            .ToList();

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = DateTime.Now;
        Upcoming = (await db.Reservations.AsNoTracking()
                .Include(item => item.Customer)
                .Include(item => item.ReservationTables)
                .Where(item => item.CustomerId == customerId
                    && item.Status != ReservationStatus.Cancelled
                    && item.Status != ReservationStatus.Completed)
                .ToListAsync())
            .Select(ReservationSummary.From)
            .Where(item => item.ReservationAt > now)
            .OrderBy(item => item.ReservationAt)
            .ToList();
    }

    private Task<List<BusinessHours>> LoadHoursAsync() =>
        db.BusinessHours.AsNoTracking().ToListAsync();

    // Every bookable time on a day. A time has room when the seats left then
    // (tables not held by an overlapping booking, minus guests still waiting for a table) fit the party.
    private async Task<List<Slot>> GetSlotsAsync(DateOnly date, int guests)
    {
        var hours = (await LoadHoursAsync()).FirstOrDefault(day => day.DayOfWeek == date.DayOfWeek);
        if (hours is { IsClosed: true })
        {
            return [];
        }

        var open = hours?.OpenTime ?? DefaultOpen;
        var close = hours?.CloseTime ?? DefaultClose;
        var last = close.Add(-LastBookingBeforeClose);

        var tables = await db.Tables.AsNoTracking().ToListAsync();
        var bookings = (await db.Reservations.AsNoTracking()
                .Include(item => item.ReservationTables)
                .Where(item => item.Date >= date.AddDays(-1) && item.Date <= date.AddDays(1)
                    && item.Status != ReservationStatus.Cancelled
                    && item.Status != ReservationStatus.Completed)
                .ToListAsync())
            .Select(ReservationSummary.From)
            .ToList();

        var now = DateTime.Now;
        var slots = new List<Slot>();
        for (var time = open; time <= last && time >= open; time = time.Add(SlotStep))
        {
            var start = date.ToDateTime(time);
            var overlapping = bookings.Where(booking => booking.Overlaps(start)).ToList();
            var heldTables = overlapping.SelectMany(booking => booking.TableIds).ToHashSet();
            var waitingGuests = overlapping.Where(booking => !booking.HasTable).Sum(booking => booking.PartySize);
            var freeSeats = tables.Where(table => !heldTables.Contains(table.Id)).Sum(table => table.Capacity) - waitingGuests;

            slots.Add(new Slot(time, start > now && freeSeats >= guests));
        }

        return slots;
    }

    private sealed record Slot(TimeOnly Time, bool Available);

    public sealed class BookingInput
    {
        [Range(1, MaxPartySize, ErrorMessage = "Guests must be between 1 and 20.")]
        public int Guests { get; set; } = 2;

        // Left at their defaults when nothing was picked; OnPostAsync rejects those.

        public DateOnly Date { get; set; }

        public TimeOnly Time { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}
