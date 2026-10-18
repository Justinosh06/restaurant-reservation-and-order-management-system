using System.Security.Claims;
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

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        // One box per session, newest visit first.
        Visits = orders
            .Where(o => o.Session is not null)
            .GroupBy(o => o.SessionId)
            .Select(g => new DiningVisit(g.First().Session!, g.OrderBy(o => o.CreatedAt).ToList()))
            .OrderByDescending(v => v.Session.StartingTime)
            .ToList();
    }
}
