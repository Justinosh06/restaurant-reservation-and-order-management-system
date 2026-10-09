using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Restaurant_Reservation_and_Order_Management_System.Data;
using Restaurant_Reservation_and_Order_Management_System.Models;
using ReservationRecord = Restaurant_Reservation_and_Order_Management_System.Models.Reservation;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Reservation;

public class ManagementModel(ReservationStore reservationStore) : PageModel
{
    public List<RestaurantTable> Tables { get; private set; } = [];

    public List<ReservationRecord> Reservations { get; private set; } = [];

    public bool IsFormOpen { get; private set; }

    public string FormMode { get; private set; } = "create";

    [BindProperty]
    public ReservationInput Input { get; set; } = new();

    [BindProperty]
    public Guid ReservationId { get; set; }

    [BindProperty]
    public int? FilterTableId { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(int? tableId, bool newReservation = false, Guid? edit = null)
    {
        await LoadPageAsync();
        FilterTableId = Tables.Any(table => table.Id == tableId) ? tableId : null;

        if (edit.HasValue)
        {
            var reservation = Reservations.FirstOrDefault(item => item.Id == edit.Value);
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
            Input.TableId = FilterTableId ?? Tables[0].Id;
            Input.ReservationAt = DateTime.Now.AddHours(1);
            IsFormOpen = true;
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        FormMode = "create";
        IsFormOpen = true;
        await LoadPageAsync();

        if (!ValidateReservation())
        {
            return Page();
        }

        await reservationStore.AddAsync(ToReservation());
        StatusMessage = "Reservation created.";
        return RedirectToPage(new { tableId = FilterTableId });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        FormMode = "edit";
        IsFormOpen = true;
        await LoadPageAsync();

        if (!ValidateReservation())
        {
            return Page();
        }

        var updated = await reservationStore.UpdateAsync(ToReservation(ReservationId));
        if (!updated)
        {
            ModelState.AddModelError(string.Empty, "This reservation no longer exists. Refresh and try again.");
            return Page();
        }

        StatusMessage = "Reservation updated.";
        return RedirectToPage(new { tableId = FilterTableId });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (await reservationStore.DeleteAsync(ReservationId))
        {
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
        Tables = RestaurantTableCatalog.GetAll();
        Reservations = await reservationStore.GetAllAsync();
    }

    private bool ValidateReservation()
    {
        var table = Tables.FirstOrDefault(item => item.Id == Input.TableId);
        if (table is null)
        {
            ModelState.AddModelError("Input.TableId", "Select a valid table.");
        }
        else if (Input.PartySize > table.Capacity)
        {
            ModelState.AddModelError("Input.PartySize", $"Table {table.Id} seats up to {table.Capacity} guests.");
        }

        if (Input.ReservationAt < DateTime.Now)
        {
            ModelState.AddModelError("Input.ReservationAt", "Choose a future date and time.");
        }

        return ModelState.IsValid;
    }

    private ReservationRecord ToReservation(Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TableId = Input.TableId,
            CustomerName = Input.CustomerName.Trim(),
            ContactNumber = NormalizeOptional(Input.ContactNumber),
            PartySize = Input.PartySize,
            ReservationAt = Input.ReservationAt,
            Notes = NormalizeOptional(Input.Notes)
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class ReservationInput
    {
        [Range(1, 16)]
        public int TableId { get; set; } = 1;

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
