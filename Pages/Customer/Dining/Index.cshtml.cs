using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer.Dining;

// Dining history: one box per visit to the restaurant. Each box opens the visit with its orders.
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public IReadOnlyList<DiningVisit> Visits { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;

        // Sample history for now; read the customer's own sessions and orders once order processing saves real ones.
        Visits = SampleOrders.Visits;
    }
}
