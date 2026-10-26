using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Reviews;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Menu;

// One dish on the online menu (/Menu/{menuItemId}): its name, picture, price, About text and customer reviews.
// Nothing can be ordered here.
public class ItemModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ItemModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public RestaurantReservation.Models.MenuItem Item { get; private set; } = null!;
    public IReadOnlyList<RestaurantReservation.Models.Feedback> Reviews { get; private set; } = [];

    // The options each review's dish was ordered with, by feedback id (see ReviewOptions).
    public Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> ReviewOptionSets { get; private set; } = [];

    public IReadOnlyList<IReadOnlyDictionary<string, string>> OptionsFor(RestaurantReservation.Models.Feedback review) =>
        ReviewOptionSets.TryGetValue(review.Id, out var sets) ? sets : [];

    // Average star rating, rounded to one decimal place; 0 when nobody has rated the dish yet.
    public double AverageRating => Reviews.Count == 0 ? 0 : Math.Round(Reviews.Average(r => r.Rating), 1);

    public async Task<IActionResult> OnGetAsync(string menuItemId)
    {
        var item = await _db.MenuItems
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == menuItemId.ToUpper() && m.Availability);
        if (item is null)
        {
            return NotFound();
        }

        Item = item;
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;

        // Highest ratings first, like the reviews on the home page.
        Reviews = await _db.Feedbacks
            .AsNoTracking()
            .Include(f => f.Customer)
            .Where(f => f.MenuItemId == item.Id)
            .OrderByDescending(f => f.Rating)
            .ToListAsync();
        ReviewOptionSets = await ReviewOptions.LoadAsync(_db, Reviews);
        return Page();
    }
}
