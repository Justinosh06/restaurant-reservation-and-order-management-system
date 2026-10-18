using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Services;
using Restaurant_Reservation_and_Order_Management_System.Pages.Admin;
using Stripe;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Customers pick a party size, a date and a time, then pay a deposit on Stripe Checkout.
// Tables are not chosen here: the booking starts as Pending and staff seat it on Table Management.
public class ReserveModel(ApplicationDbContext db, IPaymentService payments, ILogger<ReserveModel> logger) : PageModel
{
    public const double Deposit = 20.00;

    // Seats stay held for a customer who is still on the Stripe page. Stripe expires the
    // checkout after 30 minutes; the extra 5 covers a payment that finishes right at the end.
    private static readonly TimeSpan UnpaidHold = TimeSpan.FromMinutes(35);

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
        await SettleExpiredUnpaidAsync();
        await LoadPageAsync();
    }

    // Stripe sends the customer here when they leave the checkout page without paying.
    public async Task<IActionResult> OnGetPaymentCancelledAsync(string? reservationId)
    {
        var reservation = reservationId is null ? null : await FindOwnBookingAsync(item => item.Id == reservationId);
        if (reservation is { DepositStatus: DepositStatus.Unpaid } && !await MarkPaidIfStripeSaysSoAsync(reservation))
        {
            db.Reservations.Remove(reservation);
            await db.SaveChangesAsync();
        }

        StatusMessage = reservation is { DepositStatus: DepositStatus.Paid }
            ? $"Your deposit was already paid, so your booking on {Describe(reservation)} stands."
            : "Payment cancelled, so no booking was made.";
        return RedirectToPage();
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

        // Saved first so the seats are held while the customer pays; it only counts once paid.
        var reservation = new Reservation
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = Input.Date,
            TimeSlot = Input.Time,
            Pax = Input.Guests,
            Status = ReservationStatus.Pending,
            CustomerId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            DepositAmount = (int)(Deposit * 100),
            DepositStatus = DepositStatus.Unpaid
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        var page = $"{Request.Scheme}://{Request.Host}/Customer/Reserve";
        try
        {
            var session = await payments.CreateReservationDepositSessionAsync(
                reservation.Id,
                User.FindFirstValue(ClaimTypes.Name) ?? "Guest",
                User.FindFirstValue(ClaimTypes.Email),
                Deposit,
                successUrl: $"{Request.Scheme}://{Request.Host}/Payment/{{CHECKOUT_SESSION_ID}}",
                cancelUrl: $"{page}?handler=PaymentCancelled&reservationId={reservation.Id}");
            reservation.StripeSessionId = session.Id;
            await db.SaveChangesAsync();
            return Redirect(session.Url);
        }
        catch (Exception error) when (error is StripeException or InvalidOperationException)
        {
            logger.LogError(error, "Could not start the Stripe deposit payment for reservation {ReservationId}.", reservation.Id);
            db.Reservations.Remove(reservation);
            await db.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, "We couldn't open the payment page. Please try again in a moment.");
            await LoadPageAsync();
            return Page();
        }
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

        // The deposit is refunded in full; if Stripe refuses, the booking stays so nothing is lost.
        var refunded = false;
        if (reservation.DepositStatus == DepositStatus.Paid && reservation.StripeSessionId is not null)
        {
            try
            {
                await payments.RefundCheckoutSessionAsync(reservation.StripeSessionId);
                reservation.DepositStatus = DepositStatus.Refunded;
                refunded = true;
            }
            catch (Exception error) when (error is StripeException or InvalidOperationException)
            {
                logger.LogError(error, "Could not refund the deposit for reservation {ReservationId}.", reservation.Id);
                StatusMessage = "We couldn't refund your deposit just now, so your booking is still on. Please try again or call us.";
                return RedirectToPage();
            }
        }

        // Kept as Cancelled (not deleted) so staff can still see it; cancelled bookings don't hold tables.
        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();
        StatusMessage = $"Your booking on {at} is cancelled."
            + (refunded ? $" Your RM{Deposit:0} deposit is on its way back to your card (usually 5-10 working days)." : "");
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

    private Task<Reservation?> FindOwnBookingAsync(System.Linq.Expressions.Expression<Func<Reservation, bool>> match)
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return db.Reservations.IgnoreQueryFilters()
            .Where(item => item.CustomerId == customerId)
            .FirstOrDefaultAsync(match);
    }

    // Asks Stripe whether the deposit was paid (the customer may have closed the tab before coming back).
    private async Task<bool> MarkPaidIfStripeSaysSoAsync(Reservation reservation)
    {
        if (reservation.StripeSessionId is null)
        {
            return false;
        }

        try
        {
            var session = await payments.GetCheckoutSessionAsync(reservation.StripeSessionId);
            if (session.PaymentStatus != "paid")
            {
                return false;
            }
        }
        catch (StripeException error)
        {
            logger.LogError(error, "Could not check the Stripe payment for reservation {ReservationId}.", reservation.Id);
            return false;
        }

        reservation.DepositStatus = DepositStatus.Paid;
        await db.SaveChangesAsync();
        return true;
    }

    // Unpaid bookings older than the hold are either paid after all or dropped, freeing their seats.
    private async Task SettleExpiredUnpaidAsync()
    {
        var cutoff = DateTime.UtcNow - UnpaidHold;
        var expired = await db.Reservations.IgnoreQueryFilters()
            .Where(item => item.DepositStatus == DepositStatus.Unpaid && item.CreatedAt < cutoff)
            .ToListAsync();
        foreach (var reservation in expired)
        {
            if (!await MarkPaidIfStripeSaysSoAsync(reservation))
            {
                db.Reservations.Remove(reservation);
            }
        }
        await db.SaveChangesAsync();
    }

    private static string Describe(Reservation reservation) =>
        reservation.Date.ToDateTime(reservation.TimeSlot).ToString("dddd d MMMM 'at' h:mm tt", CultureInfo.CurrentCulture);

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
        // Bookings still being paid for hold their seats too, until the hold runs out.
        var holdCutoff = DateTime.UtcNow - UnpaidHold;
        var bookings = (await db.Reservations.AsNoTracking().IgnoreQueryFilters()
                .Include(item => item.ReservationTables)
                .Where(item => item.Date >= date.AddDays(-1) && item.Date <= date.AddDays(1)
                    && (item.DepositStatus != DepositStatus.Unpaid || item.CreatedAt >= holdCutoff)
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
