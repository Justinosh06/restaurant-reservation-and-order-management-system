using System.Security.Claims;
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
        // Only the customer's own orders in this session; another customer's visit is not found.
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .Where(o => o.SessionId == id && o.CustomerId == customerId)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
        if (orders.Count == 0 || orders[0].Session is null)
        {
            return NotFound();
        }

        Visit = new DiningVisit(orders[0].Session!, orders);

        var orderIds = orders.Select(o => o.Id).ToList();
        OrderItems = (await _db.OrderItems
                .AsNoTracking()
                .Where(i => orderIds.Contains(i.OrderId))
                .ToListAsync())
            .ToLookup(i => i.OrderId);
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;
        return Page();
    }
}
