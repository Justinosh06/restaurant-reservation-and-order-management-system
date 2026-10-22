using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// Home page. Everyone sees the landing page; for a logged-in customer it is their dashboard.
// Never cached: the Back button must not bring back the visitor version after the customer has logged in.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private const int ShortMenuSize = 5;
    private const int ChefSpecialsSize = 2;

    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string RestaurantName { get; private set; } = "Resto";
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public RestaurantSettings? Settings { get; private set; }
    public IReadOnlyList<BusinessHours> OpeningHours { get; private set; } = [];
    // Today's menu: the best-rated dishes first.
    public IReadOnlyList<MenuItem> ShortMenu { get; private set; } = [];

    // Average stars and number of reviews per menu item id, for the dishes in ShortMenu that have reviews.
    public IReadOnlyDictionary<string, (double Average, int Count)> Ratings { get; private set; } =
        new Dictionary<string, (double, int)>();
    public IReadOnlyList<MenuItem> ChefSpecials { get; private set; } = [];
    public IReadOnlyList<Feedback> Reviews { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Settings = await _db.RestaurantSettings
            .AsNoTracking()
            .Include(s => s.BusinessHours)
            .FirstOrDefaultAsync();

        if (Settings is not null)
        {
            RestaurantName = Settings.Name;
            Currency = Settings.DefaultCurrency;
            // Monday first, Sunday last (DayOfWeek counts Sunday as 0).
            OpeningHours = Settings.BusinessHours.OrderBy(h => ((int)h.DayOfWeek + 6) % 7).ToList();
        }

        var available = _db.MenuItems.AsNoTracking().Where(m => m.Availability);

        // Today's menu: highest average rating first, then the most reviewed; dishes nobody has rated yet fill
        // any places left, by name.
        var ratings = await _db.Feedbacks
            .AsNoTracking()
            .GroupBy(f => f.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, Average = g.Average(f => (double)f.Rating), Count = g.Count() })
            .ToDictionaryAsync(r => r.MenuItemId, r => (r.Average, r.Count));
        ShortMenu = (await available.ToListAsync())
            .OrderByDescending(m => ratings.TryGetValue(m.Id, out var r) ? r.Average : 0)
            .ThenByDescending(m => ratings.TryGetValue(m.Id, out var r) ? r.Count : 0)
            .ThenBy(m => m.Name)
            .Take(ShortMenuSize)
            .ToList();
        Ratings = ratings;

        // Chef's specials: the newest dishes on the menu, newest first.
        ChefSpecials = await available
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(ChefSpecialsSize)
            .ToListAsync();

        // Every review is loaded: the page shows them in a scrolling list with a filter by rating.
        Reviews = await _db.Feedbacks
            .AsNoTracking()
            .Include(f => f.MenuItem)
            .Include(f => f.Customer)
            .OrderByDescending(f => f.Rating)
            .ToListAsync();
    }
}
