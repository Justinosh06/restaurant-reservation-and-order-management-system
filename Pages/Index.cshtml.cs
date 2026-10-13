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

    // The menu page is opened per table (/Table/{tableId}); online customers use this one until tables are assigned.
    public const string MenuTableId = "T1";

    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string RestaurantName { get; private set; } = "Resto";
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public RestaurantSettings? Settings { get; private set; }
    public IReadOnlyList<BusinessHours> OpeningHours { get; private set; } = [];
    public IReadOnlyList<MenuItem> ShortMenu { get; private set; } = [];
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
            OpeningHours = Settings.BusinessHours.OrderBy(h => h.DayOfWeek).ToList();
        }

        var available = _db.MenuItems.AsNoTracking().Where(m => m.Availability);

        ShortMenu = await available
            .OrderBy(m => m.Name)
            .Take(ShortMenuSize)
            .ToListAsync();

        ChefSpecials = await available
            .Where(m => m.ThumbnailImage != null && m.ThumbnailImage != "")
            .OrderByDescending(m => m.Price)
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
