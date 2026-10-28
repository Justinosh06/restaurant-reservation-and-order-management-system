using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly DiningSessionService _diningSessions;
    private readonly RestaurantCurrency _restaurantCurrency;
    private readonly RestaurantOrderingHours _orderingHours;

    public IndexModel(
        ApplicationDbContext db,
        DiningSessionService diningSessions,
        RestaurantCurrency restaurantCurrency,
        RestaurantOrderingHours orderingHours)
    {
        _db = db;
        _diningSessions = diningSessions;
        _restaurantCurrency = restaurantCurrency;
        _orderingHours = orderingHours;
    }

    public string TableId { get; set; } = string.Empty;
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public string CurrencySymbol => Currency.GetSymbol();
    public bool HasActiveSession { get; private set; }
    public bool IsRestaurantClosed { get; private set; }
    public List<RestaurantReservation.Models.MenuItem> MenuItems { get; private set; } = [];
    public List<string> TopRatedMenuItemIds { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpper();
        if (!await _db.Tables.AnyAsync(table => table.Id == TableId))
        {
            return NotFound();
        }

        var activeSession = await _diningSessions.GetActiveSessionAsync(TableId);
        if (await _orderingHours.IsOrderingClosedAsync())
        {
            IsRestaurantClosed = true;
            if (activeSession is not null && await _diningSessions.HasUnpaidOrdersAsync(activeSession.Id))
            {
                return RedirectToPage("/Table/Checkout", new { tableId = TableId });
            }

            return Page();
        }

        if (activeSession is null)
        {
            return Page();
        }

        Currency = await _restaurantCurrency.GetAsync();
        HasActiveSession = true;
        ViewData["CartItemCount"] = await _diningSessions.GetCartItemCountAsync(activeSession.Id);

        MenuItems = await _db.MenuItems
            .AsNoTracking()
            .Where(item => item.Availability)
            .OrderBy(item => item.Name)
            .ToListAsync();

        TopRatedMenuItemIds = await _db.Feedbacks
            .AsNoTracking()
            .Where(feedback => _db.MenuItems.Any(item =>
                item.Id == feedback.MenuItemId && item.Availability))
            .GroupBy(feedback => feedback.MenuItemId)
            .Select(group => new
            {
                MenuItemId = group.Key,
                AverageRating = group.Average(feedback => feedback.Rating),
                RatingCount = group.Count()
            })
            .OrderByDescending(rating => rating.AverageRating)
            .ThenByDescending(rating => rating.RatingCount)
            .ThenBy(rating => rating.MenuItemId)
            .Take(5)
            .Select(rating => rating.MenuItemId)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostStartSessionAsync(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpperInvariant();
        if (!await _db.Tables.AnyAsync(table => table.Id == TableId))
        {
            return NotFound();
        }

        if (await _orderingHours.IsOrderingClosedAsync())
        {
            return RedirectToPage("/Table/Index", new { tableId = TableId });
        }

        await _diningSessions.StartSessionAsync(TableId);
        return RedirectToPage("/Table/Index", new { tableId = TableId });
    }

}