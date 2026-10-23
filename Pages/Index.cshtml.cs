using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// Home page. Everyone sees the landing page; for a logged-in customer it is their dashboard.
// Never cached: the Back button must not bring back the visitor version after the customer has logged in.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private const int TopRatedSize = 5;
    private const int NewArrivalsSize = 2;

    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string RestaurantName { get; private set; } = "Resto";
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public RestaurantSettings? Settings { get; private set; }
    public IReadOnlyList<BusinessHours> OpeningHours { get; private set; } = [];

    // Top Rating: the best-rated dishes first.
    public IReadOnlyList<MenuItem> TopRated { get; private set; } = [];

    // Average stars and number of reviews per menu item id, for the dishes that have reviews.
    public IReadOnlyDictionary<string, (double Average, int Count)> Ratings { get; private set; } =
        new Dictionary<string, (double, int)>();

    // New Arrival: the newest dishes on the menu, newest first.
    public IReadOnlyList<MenuItem> NewArrivals { get; private set; } = [];

    public IReadOnlyList<Feedback> Reviews { get; private set; } = [];

    // The booking card follows the same rules as the booking page (Pages/Customer/Reserve).
    public DateOnly FirstBookingDay => DateOnly.FromDateTime(DateTime.Now).AddDays(ReserveModel.MinDaysAhead);
    public DateOnly LastBookingDay => DateOnly.FromDateTime(DateTime.Now).AddDays(ReserveModel.DaysAhead);

    // Weekdays (0 = Sunday) the restaurant is closed, greyed out in the booking calendar.
    public IReadOnlyList<int> ClosedDays { get; private set; } = [];

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
            ClosedDays = Settings.BusinessHours.Where(h => h.IsClosed).Select(h => (int)h.DayOfWeek).ToList();
        }

        var available = _db.MenuItems.AsNoTracking().Where(m => m.Availability);

        // Top Rating: highest average rating first, then the most reviewed; dishes nobody has rated yet fill
        // any places left, by name.
        var ratings = await _db.Feedbacks
            .AsNoTracking()
            .GroupBy(f => f.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, Average = g.Average(f => (double)f.Rating), Count = g.Count() })
            .ToDictionaryAsync(r => r.MenuItemId, r => (r.Average, r.Count));
        TopRated = (await available.ToListAsync())
            .OrderByDescending(m => ratings.TryGetValue(m.Id, out var r) ? r.Average : 0)
            .ThenByDescending(m => ratings.TryGetValue(m.Id, out var r) ? r.Count : 0)
            .ThenBy(m => m.Name)
            .Take(TopRatedSize)
            .ToList();
        Ratings = ratings;

        NewArrivals = await available
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(NewArrivalsSize)
            .ToListAsync();

        // Every review is loaded: the page shows them in a scrolling list with a filter by rating.
        Reviews = await _db.Feedbacks
            .AsNoTracking()
            .Include(f => f.MenuItem)
            .Include(f => f.Customer)
            .OrderByDescending(f => f.Rating)
            .ToListAsync();
    }

    // GET /?handler=Slots&date=2026-10-30 - the booking times on that day, grouped into Morning, Afternoon and
    // Evening for the booking card's Time list. Same times as the booking page, which also checks which are free.
    public async Task<IActionResult> OnGetSlotsAsync(DateOnly date)
    {
        if (date < FirstBookingDay || date > LastBookingDay)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var hours = await RestaurantReservation.Infrastructure.Reservations.OpeningHours.ForDateAsync(_db, date);
        if (hours.IsClosed)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var times = new List<TimeOnly>();
        for (var time = hours.Open; time <= hours.LastBooking && time >= hours.Open; time = time.Add(ReserveModel.SlotStep))
        {
            times.Add(time);
        }

        static string Section(TimeOnly time) => time.Hour < 12 ? "Morning" : time.Hour < 17 ? "Afternoon" : "Evening";
        return new JsonResult(times
            .GroupBy(Section)
            .Select(group => new
            {
                section = group.Key,
                times = group.Select(time => new
                {
                    value = time.ToString("HH:mm", CultureInfo.InvariantCulture),
                    label = time.ToString("h:mm tt", CultureInfo.InvariantCulture)
                })
            }));
    }
}
