using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Models;
using RestaurantReservation.Services;
using Stripe;
// The Razor pages in Pages/Table compile into a namespace called Table, which hides the model's short name here.
using DiningTable = RestaurantReservation.Models.Table;

// Not ...Pages.Admin.Reservation: a namespace called Reservation would hide the Reservation model from other admin pages.
namespace Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

public class ReservationManagementModel(
    ApplicationDbContext db,
    IPaymentService payments,
    ILogger<ReservationManagementModel> logger) : PageModel
{
    // The list tabs: upcoming bookings already seated at a table, upcoming ones still
    // waiting for a table, bookings whose time has passed, and cancelled ones.
    public static readonly (string Key, string Label)[] Tabs =
    [
        ("upcoming", "Upcoming"),
        ("pending", "Pending"),
        ("past", "Past"),
        ("cancelled", "Cancelled")
    ];

    public List<DiningTable> Tables { get; private set; } = [];

    public List<ReservationSummary> Reservations { get; private set; } = [];

    public bool IsFormOpen { get; private set; }

    public string FormMode { get; private set; } = "create";

    [BindProperty]
    public ReservationInput Input { get; set; } = new();

    [BindProperty]
    public string? ReservationId { get; set; }

    [BindProperty]
    public string? FilterTableId { get; set; }

    // Only remembers which tab to return to, so it is optional (string?) and a blank value
    // falls back to Upcoming instead of failing the form as a required field.
    [BindProperty(SupportsGet = true)]
    public string? Tab
    {
        get => tab;
        set => tab = string.IsNullOrWhiteSpace(value) ? "upcoming" : value;
    }

    private string tab = "upcoming";

    // Set when staff were warned that the time is outside opening hours; saving again with the same
    // time keeps it (walk-ins and phone bookings sometimes need that).
    [BindProperty]
    public string? OutsideHoursOkFor { get; set; }

    // The reservations on the open tab (after the table filter), in the order they are listed.
    public List<ReservationSummary> TabReservations { get; private set; } = [];

    public Dictionary<string, int> TabCounts { get; private set; } = [];

    // For each upcoming or pending reservation on the open tab: the tables free at its time
    // (its own tables included) and the tables suggested for it.
    public Dictionary<string, List<string>> FreeTables { get; private set; } = [];

    public Dictionary<string, List<string>> SuggestedTables { get; private set; } = [];

    public static string TabOf(ReservationSummary reservation, DateTime now) =>
        reservation.Status == ReservationStatus.Cancelled ? "cancelled"
        : reservation.Status == ReservationStatus.Completed || !reservation.IsActive(now) ? "past"
        : reservation.HasTable ? "upcoming"
        : "pending";

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? tableId, bool newReservation = false, string? edit = null)
    {
        await LoadPageAsync();
        FilterTableId = Tables.FirstOrDefault(table => table.Id == tableId)?.Id;
        BuildTabs();

        if (!string.IsNullOrEmpty(edit))
        {
            var reservation = Reservations.FirstOrDefault(item => item.Id == edit);
            if (reservation is not null)
            {
                ReservationId = reservation.Id;
                Input = new ReservationInput
                {
                    TableIds = reservation.TableIds.ToList(),
                    CustomerName = reservation.CustomerName,
                    ContactNumber = reservation.ContactNumber,
                    PartySize = reservation.PartySize,
                    ReservationAt = reservation.ReservationAt,
                    Notes = reservation.Notes
                };
                FormMode = "edit";
                IsFormOpen = true;
            }
            else
            {
                StatusMessage = "That reservation could not be found.";
            }
        }
        else if (newReservation)
        {
            Input.TableIds = FilterTableId is null ? [] : [FilterTableId];
            Input.ReservationAt = DateTime.Now.AddHours(1);
            IsFormOpen = true;
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        FormMode = "create";
        IsFormOpen = true;
        await LoadPageAsync();

        if (!ValidateReservation(existing: null) || !await CheckOpeningHoursAsync())
        {
            return Page();
        }

        var reservation = new RestaurantReservation.Models.Reservation { Status = ReservationStatus.Confirmed };
        ApplyInput(reservation);
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        StatusMessage = "Reservation created.";
        return RedirectToPage(new { tableId = FilterTableId, tab = TabOf(ReservationSummary.From(reservation), DateTime.Now) });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        FormMode = "edit";
        IsFormOpen = true;
        await LoadPageAsync();

        var reservation = ReservationId is null ? null : await db.Reservations
            .Include(item => item.ReservationTables)
            .FirstOrDefaultAsync(item => item.Id == ReservationId);
        if (reservation is null)
        {
            ModelState.AddModelError(string.Empty, "This reservation no longer exists. Refresh and try again.");
            return Page();
        }

        if (!ValidateReservation(reservation) || !await CheckOpeningHoursAsync())
        {
            return Page();
        }

        ApplyInput(reservation);
        await db.SaveChangesAsync();

        StatusMessage = "Reservation updated.";
        return RedirectToPage(new { tableId = FilterTableId, tab = TabOf(ReservationSummary.From(reservation), DateTime.Now) });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var reservation = ReservationId is null ? null : await db.Reservations.FindAsync(ReservationId);
        if (reservation is { DepositStatus: DepositStatus.Paid })
        {
            // Deleting would keep the customer's money with no booking; Cancel refunds it first.
            StatusMessage = "This booking has a paid deposit, so it can't be deleted. Cancel it instead — the deposit is refunded automatically.";
        }
        else if (reservation is not null)
        {
            db.Reservations.Remove(reservation);
            await db.SaveChangesAsync();
            StatusMessage = "Reservation deleted.";
        }
        else
        {
            StatusMessage = "That reservation was already deleted.";
        }

        return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
    }

    // Seats a reservation at the given tables (several for a large party), or takes it off its
    // tables when none are given. Staff stay on the tab they were working through.
    public async Task<IActionResult> OnPostAssignAsync(string[] tableIds)
    {
        var reservation = ReservationId is null ? null : await db.Reservations
            .Include(item => item.Customer)
            .Include(item => item.ReservationTables)
            .FirstOrDefaultAsync(item => item.Id == ReservationId);
        if (reservation is null || reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
        {
            StatusMessage = "That reservation can no longer be seated.";
            return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
        }

        var summary = ReservationSummary.From(reservation);
        var label = $"{summary.CustomerName} ({summary.PartySize} guests, "
            + $"{summary.ReservationAt.ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)})";

        var wanted = tableIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (wanted.Count == 0)
        {
            reservation.ReservationTables.Clear();
            await db.SaveChangesAsync();
            StatusMessage = $"{label} no longer has a table, so it is back in Pending.";
            return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
        }

        var tables = await db.Tables.Where(table => wanted.Contains(table.Id)).ToListAsync();
        if (tables.Count != wanted.Count)
        {
            StatusMessage = "One of those tables no longer exists.";
            return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
        }

        var seats = tables.Sum(table => table.Capacity);
        if (reservation.Pax > seats)
        {
            StatusMessage = $"{label} needs {reservation.Pax} seats, but {TablesLabel(wanted)} only seat{(wanted.Count == 1 ? "s" : "")} {seats}.";
            return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
        }

        var others = (await db.Reservations.AsNoTracking()
                .Include(item => item.Customer)
                .Include(item => item.ReservationTables)
                .Where(item => item.Id != reservation.Id
                    && item.ReservationTables.Any(link => wanted.Contains(link.TableId)))
                .ToListAsync())
            .Select(ReservationSummary.From)
            .ToList();
        foreach (var tableId in wanted)
        {
            var clash = ReservationSummary.FindClash(others, tableId, summary.ReservationAt, reservation.Id);
            if (clash is not null)
            {
                StatusMessage = $"Table {tableId} is already booked by {clash.CustomerName} at "
                    + $"{clash.ReservationAt.ToString("h:mm tt", CultureInfo.CurrentCulture)}.";
                return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
            }
        }

        var wasPending = !summary.HasTable;
        // Seating a booking confirms it, so the customer sees "Confirmed" instead of waiting.
        if (reservation.Status == ReservationStatus.Pending)
        {
            reservation.Status = ReservationStatus.Confirmed;
        }
        reservation.ReservationTables.RemoveAll(link => !wanted.Contains(link.TableId));
        foreach (var tableId in wanted.Where(id => reservation.ReservationTables.All(link => link.TableId != id)))
        {
            // Id is set here, not on save: EF cannot track two new links that both have an empty Id.
            reservation.ReservationTables.Add(new ReservationTable { Id = Guid.NewGuid().ToString("N"), TableId = tableId });
        }
        await db.SaveChangesAsync();
        StatusMessage = $"{label} is now at {TablesLabel(wanted)}" + (wasPending ? ", confirmed, and has moved to Upcoming." : ".");
        return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
    }

    private static string TablesLabel(IReadOnlyCollection<string> tableIds) =>
        (tableIds.Count == 1 ? "table " : "tables ") + string.Join(" + ", tableIds);

    // Cancels a booking but keeps it (it moves to the Cancelled tab). A paid deposit is refunded in full.
    public async Task<IActionResult> OnPostCancelAsync()
    {
        var reservation = ReservationId is null ? null : await db.Reservations
            .Include(item => item.Customer)
            .FirstOrDefaultAsync(item => item.Id == ReservationId);
        if (reservation is null || reservation.Status == ReservationStatus.Cancelled)
        {
            StatusMessage = "That reservation is already cancelled or gone.";
            return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
        }

        var summary = ReservationSummary.From(reservation);
        var label = $"{summary.CustomerName}'s booking on "
            + summary.ReservationAt.ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture);

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
                StatusMessage = $"Couldn't refund the deposit for {label}, so it was not cancelled. Try again, or refund it in the Stripe dashboard.";
                return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
            }
        }

        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();
        StatusMessage = $"{label} is cancelled." + (refunded ? " The deposit was refunded." : "");
        return RedirectToPage(new { tableId = FilterTableId, tab = Tab });
    }

    private async Task LoadPageAsync()
    {
        Tables = await db.Tables.AsNoTracking()
            .OrderBy(table => table.CoordinateY)
            .ThenBy(table => table.CoordinateX)
            .ThenBy(table => table.Id)
            .ToListAsync();

        var reservations = await db.Reservations.AsNoTracking()
            .Include(reservation => reservation.Customer)
            .Include(reservation => reservation.ReservationTables)
            .ToListAsync();
        Reservations = reservations.Select(ReservationSummary.From).ToList();
        BuildTabs();
    }

    private void BuildTabs()
    {
        if (!Tabs.Any(tab => tab.Key == Tab))
        {
            Tab = "upcoming";
        }

        var now = DateTime.Now;
        var shown = Reservations
            .Where(reservation => FilterTableId is null || reservation.TableIds.Contains(FilterTableId))
            .ToList();
        TabCounts = Tabs.ToDictionary(
            tab => tab.Key,
            tab => shown.Count(reservation => TabOf(reservation, now) == tab.Key));

        // Coming bookings soonest first; past and cancelled ones most recent first.
        var onTab = shown.Where(reservation => TabOf(reservation, now) == Tab);
        TabReservations = (Tab is "past" or "cancelled"
                ? onTab.OrderByDescending(reservation => reservation.ReservationAt)
                : onTab.OrderBy(reservation => reservation.ReservationAt))
            .ToList();

        FreeTables = [];
        SuggestedTables = [];
        if (Tab is "upcoming" or "pending")
        {
            foreach (var reservation in TabReservations)
            {
                var free = Tables
                    .Where(table => ReservationSummary.FindClash(Reservations, table.Id, reservation.ReservationAt, reservation.Id) is null)
                    .ToList();
                FreeTables[reservation.Id] = free.Select(table => table.Id).ToList();
                SuggestedTables[reservation.Id] = reservation.HasTable
                    ? reservation.TableIds.ToList()
                    : Suggest(free, reservation.PartySize);
            }
        }
    }

    // The smallest free table that seats the whole party; when none does, the biggest free
    // tables until everyone fits. Empty when even all free tables together are too small.
    private static List<string> Suggest(List<DiningTable> free, int partySize)
    {
        var single = free
            .Where(table => table.Capacity >= partySize)
            .OrderBy(table => table.Capacity)
            .FirstOrDefault();
        if (single is not null)
        {
            return [single.Id];
        }

        var picked = new List<string>();
        var seats = 0;
        foreach (var table in free.OrderByDescending(table => table.Capacity))
        {
            picked.Add(table.Id);
            seats += table.Capacity;
            if (seats >= partySize)
            {
                return picked;
            }
        }

        return [];
    }

    // existing is the reservation being edited, or null when creating one.
    private bool ValidateReservation(RestaurantReservation.Models.Reservation? existing)
    {
        // Tables are optional here; staff can assign them later on Table Management.
        // A large party can take several tables as long as they seat everyone together.
        Input.TableIds = Input.TableIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var tables = Tables.Where(item => Input.TableIds.Contains(item.Id)).ToList();
        var seats = tables.Sum(item => item.Capacity);
        if (tables.Count != Input.TableIds.Count)
        {
            ModelState.AddModelError("Input.TableIds", "Select valid tables.");
        }
        else if (tables.Count > 0 && Input.PartySize > seats)
        {
            ModelState.AddModelError("Input.PartySize", tables.Count == 1
                ? $"Table {tables[0].Id} seats up to {seats} guests."
                : $"Tables {string.Join(" + ", Input.TableIds)} seat up to {seats} guests together.");
        }

        // A past reservation can still be edited as long as its date and time are left alone.
        var timeUnchanged = existing is not null
            && existing.Date == DateOnly.FromDateTime(Input.ReservationAt)
            && existing.TimeSlot.Hour == Input.ReservationAt.Hour
            && existing.TimeSlot.Minute == Input.ReservationAt.Minute;
        if (!timeUnchanged && Input.ReservationAt < DateTime.Now)
        {
            ModelState.AddModelError("Input.ReservationAt", "Choose a future date and time.");
        }

        foreach (var table in tables)
        {
            var clash = ReservationSummary.FindClash(Reservations, table.Id, Input.ReservationAt, existing?.Id);
            if (clash is not null)
            {
                ModelState.AddModelError("Input.ReservationAt",
                    $"Table {table.Id} is already booked at {clash.ReservationAt.ToString("h:mm tt", CultureInfo.CurrentCulture)}. "
                    + $"Bookings on the same table must be {ReservationSummary.SlotLength.TotalHours:0} hours apart.");
                break;
            }
        }

        return ModelState.IsValid;
    }

    // Warns once when the time is outside opening hours (or the restaurant is closed that day).
    private async Task<bool> CheckOpeningHoursAsync()
    {
        var at = Input.ReservationAt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        if (OutsideHoursOkFor == at)
        {
            return true;
        }

        var hours = await OpeningHours.ForDateAsync(db, DateOnly.FromDateTime(Input.ReservationAt));
        var time = TimeOnly.FromDateTime(Input.ReservationAt);
        var warning = hours.IsClosed
            ? $"The restaurant is closed on {Input.ReservationAt:dddd}s."
            : time < hours.Open || time > hours.LastBooking
                ? $"This is outside opening hours ({hours.Open.ToString("h:mm tt", CultureInfo.CurrentCulture)} – "
                    + $"{hours.LastBooking.ToString("h:mm tt", CultureInfo.CurrentCulture)} for the last booking)."
                : null;
        if (warning is null)
        {
            return true;
        }

        OutsideHoursOkFor = at;
        ModelState.AddModelError("Input.ReservationAt", warning + " Press Save again to keep this time.");
        return false;
    }

    private void ApplyInput(RestaurantReservation.Models.Reservation reservation)
    {
        reservation.ReservationTables.RemoveAll(link => !Input.TableIds.Contains(link.TableId));
        foreach (var tableId in Input.TableIds.Where(id => reservation.ReservationTables.All(link => link.TableId != id)))
        {
            // Id is set here, not on save: EF cannot track two new links that both have an empty Id.
            reservation.ReservationTables.Add(new ReservationTable { Id = Guid.NewGuid().ToString("N"), TableId = tableId });
        }
        reservation.GuestName = Input.CustomerName.Trim();
        reservation.ContactNumber = NormalizeOptional(Input.ContactNumber);
        reservation.Pax = Input.PartySize;
        reservation.Date = DateOnly.FromDateTime(Input.ReservationAt);
        reservation.TimeSlot = TimeOnly.FromDateTime(Input.ReservationAt);
        reservation.Notes = NormalizeOptional(Input.Notes);

        // Seating a booking confirms it.
        if (Input.TableIds.Count > 0 && reservation.Status == ReservationStatus.Pending)
        {
            reservation.Status = ReservationStatus.Confirmed;
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class ReservationInput
    {
        public List<string> TableIds { get; set; } = [];

        [Required]
        [StringLength(80)]
        public string CustomerName { get; set; } = string.Empty;

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [Range(1, 20)]
        public int PartySize { get; set; } = 2;

        [Required]
        public DateTime ReservationAt { get; set; } = DateTime.Now.AddHours(1);

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}
