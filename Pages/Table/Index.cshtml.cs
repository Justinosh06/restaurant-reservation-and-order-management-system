using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly DiningSessionService _diningSessions;

    public IndexModel(ApplicationDbContext db, DiningSessionService diningSessions)
    {
        _db = db;
        _diningSessions = diningSessions;
    }

    public string TableId { get; set; } = string.Empty;
    public bool HasActiveSession { get; private set; }
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
        HasActiveSession = activeSession is not null;
        if (activeSession is null)
        {
            return Page();
        }

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

        TableId = tableId.ToUpper();
        if (!await _db.Tables.AnyAsync(table => table.Id == TableId))
        {
            return NotFound();
        }

        await _diningSessions.StartSessionAsync(TableId);
        return RedirectToPage("/Table/Index", new { tableId = TableId });
    }
}