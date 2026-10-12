using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// Home page. Everyone sees the landing page; a logged-in customer also gets their order and dining history.
public class IndexModel : PageModel
{
    private const int ShortMenuSize = 5;
    private const int ChefSpecialsSize = 2;
    private const int ReviewsSize = 6;
    private const int HistorySize = 10;

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

    // Customer dashboard: the latest orders, each with its dining session and ordered items.
    public IReadOnlyList<Order> Orders { get; private set; } = [];
    public ILookup<string, OrderItem> OrderItems { get; private set; } = Enumerable.Empty<OrderItem>().ToLookup(i => i.OrderId);

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

        if (User.IsInRole(AppRoles.Customer))
        {
            // Sample history for now; switch back to LoadDashboardAsync once order processing saves real orders.
            Orders = SampleOrders.All;
            OrderItems = SampleOrders.Items;
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

        Reviews = await _db.Feedbacks
            .AsNoTracking()
            .Include(f => f.MenuItem)
            .Include(f => f.Customer)
            .OrderByDescending(f => f.Rating)
            .Take(ReviewsSize)
            .ToListAsync();
    }

    private async Task LoadDashboardAsync()
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        Orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(HistorySize)
            .ToListAsync();

        var orderIds = Orders.Select(o => o.Id).ToList();
        var items = await _db.OrderItems
            .AsNoTracking()
            .Include(i => i.MenuItem)
            .Where(i => orderIds.Contains(i.OrderId))
            .ToListAsync();
        OrderItems = items.ToLookup(i => i.OrderId);
    }
}
