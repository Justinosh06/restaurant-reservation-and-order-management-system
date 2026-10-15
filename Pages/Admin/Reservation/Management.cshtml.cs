using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
// The Razor pages in Pages/Table compile into a namespace called Table, which hides the model's short name here.
using DiningTable = RestaurantReservation.Models.Table;

// Not ...Pages.Admin.Reservation: a namespace called Reservation would hide the Reservation model from other admin pages.
namespace Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

public class ReservationManagementModel(ApplicationDbContext db) : PageModel
{
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

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? tableId, bool newReservation = false, string? edit = null)
    {
        await LoadPageAsync();
        FilterTableId = Tables.FirstOrDefault(table => table.Id == tableId)?.Id;

        if (!string.IsNullOrEmpty(edit))
        {
            var reservation = Reservations.FirstOrDefault(item => item.Id == edit);
            if (reservation is not null)
            {
                ReservationId = reservation.Id;
                Input = new ReservationInput
                {
                    TableId = reservation.TableId,
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
            Input.TableId = FilterTableId ?? Tables.FirstOrDefault()?.Id ?? string.Empty;
            Input.ReservationAt = DateTime.Now.AddHours(1);
            IsFormOpen = true;
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        FormMode = "create";
        IsFormOpen = true;
        await LoadPageAsync();

        if (!ValidateReservation(existing: null))
        {
            return Page();
        }

        var reservation = new Reservation { Status = ReservationStatus.Confirmed };
        ApplyInput(reservation);
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        StatusMessage = "Reservation created.";
        return RedirectToPage(new { tableId = FilterTableId });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        FormMode = "edit";
        IsFormOpen = true;
        await LoadPageAsync();

        var reservation = ReservationId is null ? null : await db.Reservations.FindAsync(ReservationId);
        if (reservation is null)
        {
            ModelState.AddModelError(string.Empty, "This reservation no longer exists. Refresh and try again.");
            return Page();
        }

        if (!ValidateReservation(reservation))
        {
            return Page();
        }

        ApplyInput(reservation);
        await db.SaveChangesAsync();

        StatusMessage = "Reservation updated.";
        return RedirectToPage(new { tableId = FilterTableId });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var reservation = ReservationId is null ? null : await db.Reservations.FindAsync(ReservationId);
        if (reservation is not null)
        {
            db.Reservations.Remove(reservation);
            await db.SaveChangesAsync();
            StatusMessage = "Reservation deleted.";
        }
        else
        {
            StatusMessage = "That reservation was already deleted.";
        }

        return RedirectToPage(new { tableId = FilterTableId });
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
            .ToListAsync();
        Reservations = reservations.Select(ReservationSummary.From).ToList();
    }

    // existing is the reservation being edited, or null when creating one.
    private bool ValidateReservation(Reservation? existing)
    {
        // The table is optional here; staff can assign it later on Table Management.
        var table = Tables.FirstOrDefault(item => item.Id == Input.TableId);
        if (table is null && !string.IsNullOrEmpty(Input.TableId))
        {
            ModelState.AddModelError("Input.TableId", "Select a valid table.");
        }
        else if (table is not null && Input.PartySize > table.Capacity)
        {
            ModelState.AddModelError("Input.PartySize", $"Table {table.Id} seats up to {table.Capacity} guests.");
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

        var clash = table is null
            ? null
            : ReservationSummary.FindClash(Reservations, table.Id, Input.ReservationAt, existing?.Id);
        if (clash is not null)
        {
            ModelState.AddModelError("Input.ReservationAt",
                $"Table {clash.TableId} is already booked at {clash.ReservationAt.ToString("h:mm tt", CultureInfo.CurrentCulture)}. "
                + $"Bookings on the same table must be {ReservationSummary.SlotLength.TotalHours:0} hours apart.");
        }

        return ModelState.IsValid;
    }

    private void ApplyInput(Reservation reservation)
    {
        reservation.TableId = string.IsNullOrEmpty(Input.TableId) ? null : Input.TableId;
        reservation.GuestName = Input.CustomerName.Trim();
        reservation.ContactNumber = NormalizeOptional(Input.ContactNumber);
        reservation.Pax = Input.PartySize;
        reservation.Date = DateOnly.FromDateTime(Input.ReservationAt);
        reservation.TimeSlot = TimeOnly.FromDateTime(Input.ReservationAt);
        reservation.Notes = NormalizeOptional(Input.Notes);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class ReservationInput
    {
        public string? TableId { get; set; }

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
