using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Menu;

// Menu for browsing online (/Menu). Open to everyone, logged in or not. View only: ordering happens at the table (/Table/{tableId}).
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public IReadOnlyList<RestaurantReservation.Models.MenuItem> Items { get; private set; } = [];

    // Up to five best-rated dishes, best first: shown as "Top rated" when the search box is opened (same as the table menu).
    public List<string> TopRatedMenuItemIds { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;

        Items = await _db.MenuItems
            .AsNoTracking()
            .Where(m => m.Availability)
            .OrderBy(m => m.MenuItemType)
            .ThenBy(m => m.Name)
            .ToListAsync();

        TopRatedMenuItemIds = await _db.Feedbacks
            .AsNoTracking()
            .Where(f => _db.MenuItems.Any(m => m.Id == f.MenuItemId && m.Availability))
            .GroupBy(f => f.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, AverageRating = g.Average(f => f.Rating), RatingCount = g.Count() })
            .OrderByDescending(r => r.AverageRating)
            .ThenByDescending(r => r.RatingCount)
            .ThenBy(r => r.MenuItemId)
            .Take(5)
            .Select(r => r.MenuItemId)
            .ToListAsync();
    }
}
