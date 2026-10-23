using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Promotions running today, the one ending soonest first. Each box opens the same poster as the pop-up on the
// landing page (Pages/Shared/_PromotionCard), so a customer who closed it can see it again.
public class PromotionsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public PromotionsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    public IReadOnlyList<RestaurantReservation.Models.Promotion> Promotions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Promotions = await _db.Promotions
            .AsNoTracking()
            .Where(p => p.StartDate <= Today && p.EndDate >= Today)
            .OrderBy(p => p.EndDate)
            .ThenBy(p => p.Title)
            .ToListAsync();
    }
}
