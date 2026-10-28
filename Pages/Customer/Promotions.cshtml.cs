using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Promotions running today, the one ending soonest first. Each box opens the same poster as the pop-up on the
// landing page (Pages/Shared/_PromotionCard), so a customer who closed it can see it again.
public class PromotionsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly RestaurantCurrency _restaurantCurrency;

    public PromotionsModel(ApplicationDbContext db, RestaurantCurrency restaurantCurrency)
    {
        _db = db;
        _restaurantCurrency = restaurantCurrency;
    }

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public string CurrencySymbol => Currency.GetSymbol();
    public IReadOnlyList<RestaurantReservation.Models.Promotion> Promotions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Currency = await _restaurantCurrency.GetAsync();
        Promotions = await _db.Promotions
            .AsNoTracking()
            .Where(p => p.StartDate <= Today && p.EndDate >= Today)
            .OrderBy(p => p.EndDate)
            .ThenBy(p => p.Title)
            .ToListAsync();
    }
}
