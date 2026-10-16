using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Menu;

// Menu for customers browsing online (/Menu). View only: ordering happens at the table (/Table/{tableId}).
// Only customers can open it (see AuthorizeFolder in Program.cs); visitors are sent to log in first.
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public IReadOnlyList<RestaurantReservation.Models.MenuItem> Items { get; private set; } = [];

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
    }
}
