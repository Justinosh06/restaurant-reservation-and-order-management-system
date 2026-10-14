using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer.Dining;

// One dining visit: its times and the orders placed during it. Each order opens the order page.
public class VisitModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public VisitModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public DiningVisit Visit { get; private set; } = null!;
    public ILookup<string, OrderItem> OrderItems { get; private set; } = Enumerable.Empty<OrderItem>().ToLookup(i => i.OrderId);

    public async Task<IActionResult> OnGetAsync(string id)
    {
        // Sample history for now; read the customer's own session once order processing saves real ones.
        var visit = SampleOrders.FindVisit(id);
        if (visit is null)
        {
            return NotFound();
        }

        Visit = visit;
        OrderItems = SampleOrders.Items;
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;
        return Page();
    }
}
