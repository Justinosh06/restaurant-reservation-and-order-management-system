using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// Customer reviews (/Reviews), open to everyone: the overall scores at the top, how many reviews each star
// rating has, then the reviews themselves, newest first, which can be narrowed to one star rating.
public class ReviewsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ReviewsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    // ?rating=5 shows only 5-star reviews; empty shows them all.
    [BindProperty(SupportsGet = true)]
    public int? Rating { get; set; }

    public string RestaurantName { get; private set; } = "Resto";

    public int Total { get; private set; }
    public double Average { get; private set; }

    // Share of all reviews that gave 5 stars.
    public int ExcellentPercent { get; private set; }
    public int ExcellentCount { get; private set; }

    // This calendar month (local time), and the change from last month's average.
    public int ThisMonthCount { get; private set; }
    public double? ThisMonthAverage { get; private set; }
    public double? ChangeFromLastMonth { get; private set; }

    // Number of reviews per star rating, 5 down to 1.
    public IReadOnlyDictionary<int, int> Breakdown { get; private set; } = new Dictionary<int, int>();

    // The reviews shown in the list: all, or only the chosen star rating; newest first.
    public IReadOnlyList<Feedback> Reviews { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Rating is not (null or >= 1 and <= 5))
        {
            return RedirectToPage();
        }

        RestaurantName = await _db.RestaurantSettings.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync() ?? RestaurantName;

        var all = await _db.Feedbacks
            .AsNoTracking()
            .Include(f => f.Customer)
            .Include(f => f.MenuItem)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        Total = all.Count;
        Average = Total == 0 ? 0 : Math.Round(all.Average(f => f.Rating), 1);
        ExcellentCount = all.Count(f => f.Rating == 5);
        ExcellentPercent = Total == 0 ? 0 : (int)Math.Round(100.0 * ExcellentCount / Total);
        Breakdown = Enumerable.Range(1, 5).ToDictionary(stars => stars, stars => all.Count(f => f.Rating == stars));

        var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var thisMonth = all.Where(f => Local(f.CreatedAt) >= monthStart).ToList();
        var lastMonth = all.Where(f => Local(f.CreatedAt) >= monthStart.AddMonths(-1) && Local(f.CreatedAt) < monthStart).ToList();
        ThisMonthCount = thisMonth.Count;
        ThisMonthAverage = thisMonth.Count == 0 ? null : Math.Round(thisMonth.Average(f => f.Rating), 1);
        ChangeFromLastMonth = thisMonth.Count == 0 || lastMonth.Count == 0
            ? null
            : Math.Round(thisMonth.Average(f => f.Rating) - lastMonth.Average(f => f.Rating), 1);

        Reviews = Rating is { } stars ? all.Where(f => f.Rating == stars).ToList() : all;
        return Page();
    }

    private static DateTime Local(DateTime utc) => RestaurantReservation.Infrastructure.Reviews.ReviewDisplay.Local(utc);
}
