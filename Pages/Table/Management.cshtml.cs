using Microsoft.AspNetCore.Mvc.RazorPages;
using Restaurant_Reservation_and_Order_Management_System.Data;
using Restaurant_Reservation_and_Order_Management_System.Models;
using ReservationRecord = Restaurant_Reservation_and_Order_Management_System.Models.Reservation;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Table;

public class ManagementModel(ReservationStore reservationStore) : PageModel
{
    public List<RestaurantTable> Tables { get; private set; } = [];

    public List<ReservationRecord> Reservations { get; private set; } = [];

    public int? SelectedTableId { get; private set; }

    public async Task OnGetAsync(int? tableId)
    {
        Tables = RestaurantTableCatalog.GetAll();
        Reservations = await reservationStore.GetAllAsync();
        SelectedTableId = Tables.Any(table => table.Id == tableId) ? tableId : null;
    }
}
