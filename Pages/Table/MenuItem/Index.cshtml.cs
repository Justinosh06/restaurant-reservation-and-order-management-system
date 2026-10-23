using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table.MenuItem;

public class MenuItemModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly DiningSessionService _diningSessions;

    // A dish without options posts no "Options[...]" fields. Without an explicit Name, model binding then falls back
    // to binding the whole form (Quantity, Remarks, the antiforgery token) into this dictionary, so the dish looked
    // like it had unknown options and could never be added. ValidateNever skips the automatic "required" check;
    // OnPostAddToCartAsync checks the options against the dish itself.
    [BindProperty(Name = "Options")]
    [ValidateNever]
    public Dictionary<string, string> Options { get; set; } = new();

    [BindProperty]
    public string? Remarks { get; set; }

    [BindProperty]
    public int Quantity { get; set; } = 1;

    public MenuItemModel(ApplicationDbContext db, DiningSessionService diningSessions)
    {
        _db = db;
        _diningSessions = diningSessions;
    }

    public string TableId { get; set; } = string.Empty;
    public string MenuItemId { get; set; } = string.Empty;
    public string ActiveTab { get; private set; } = "Order";
    public RestaurantReservation.Models.MenuItem? Item { get; private set; }
    public double AverageRating { get; private set; }
    public int RatingCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(string tableId, string menuItemId, string? tab = "Order")
    {
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(menuItemId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpper();
        var activeSession = await _diningSessions.GetActiveSessionAsync(TableId);
        if (activeSession is null)
        {
            return RedirectToPage("/Table/Index", new { tableId = TableId });
        }

        ViewData["CartItemCount"] = await _diningSessions.GetCartItemCountAsync(activeSession.Id);
        MenuItemId = menuItemId.ToUpper();
        ActiveTab = string.Equals(tab, "About", StringComparison.OrdinalIgnoreCase) ? "About" : "Order";
        Item = await _db.MenuItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == MenuItemId && item.Availability);
        if (Item is null)
        {
            return NotFound();
        }

        if (ActiveTab == "About")
        {
            var rating = await _db.Feedbacks
                .AsNoTracking()
                .Where(feedback => feedback.MenuItemId == Item.Id)
                .GroupBy(feedback => feedback.MenuItemId)
                .Select(group => new
                {
                    AverageRating = group.Average(feedback => feedback.Rating),
                    RatingCount = group.Count()
                })
                .FirstOrDefaultAsync();

            if (rating is not null)
            {
                AverageRating = Math.Round(rating.AverageRating, 1);
                RatingCount = rating.RatingCount;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddToCartAsync(string tableId, string menuItemId)
    {
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(menuItemId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpperInvariant();
        MenuItemId = menuItemId.ToUpperInvariant();

        var activeSession = await _diningSessions.GetActiveSessionAsync(TableId);
        if (activeSession is null)
        {
            return RedirectToPage("/Table/Index", new { tableId = TableId });
        }

        ViewData["CartItemCount"] = await _diningSessions.GetCartItemCountAsync(activeSession.Id);
        Item = await _db.MenuItems
            .FirstOrDefaultAsync(item => item.Id == MenuItemId && item.Availability);
        if (Item is null)
        {
            return NotFound();
        }

        if (Quantity is < 1 or > 20)
        {
            ModelState.AddModelError(nameof(Quantity), "Choose a quantity from 1 to 20.");
        }

        if (Remarks?.Length > 300)
        {
            ModelState.AddModelError(nameof(Remarks), "Remarks cannot exceed 300 characters.");
        }

        var validOptions = Item.Options.ToDictionary(
            option => option.Key,
            option => option.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);

        if (Options.Count != validOptions.Count ||
            validOptions.Any(option =>
                !Options.TryGetValue(option.Key, out var selectedValue) ||
                !option.Value.Contains(selectedValue, StringComparer.Ordinal)) ||
            Options.Keys.Any(key => !validOptions.ContainsKey(key)))
        {
            // Added as a page-level error so the form's validation summary shows it.
            ModelState.AddModelError(string.Empty, "Select a valid option for each menu item option.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var selectedOptions = new Dictionary<string, string>(Options, StringComparer.Ordinal);
        var matchingCartItems = await _db.CartItems
            .Where(cartItem =>
                cartItem.SessionId == activeSession.Id &&
                cartItem.MenuItemId == Item.Id &&
                cartItem.Remarks == (string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim()))
            .ToListAsync();
        var existingCartItem = matchingCartItems.FirstOrDefault(cartItem =>
            cartItem.Options.Count == selectedOptions.Count &&
            cartItem.Options.All(option =>
                selectedOptions.TryGetValue(option.Key, out var value) && value == option.Value));

        if (existingCartItem is not null)
        {
            existingCartItem.Quantity = Math.Min(existingCartItem.Quantity + Quantity, 20);
        }
        else
        {
            _db.CartItems.Add(new CartItem
            {
                SessionId = activeSession.Id,
                MenuItemId = Item.Id,
                Quantity = Quantity,
                Remarks = string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(),
                Options = selectedOptions
            });
        }

        await _db.SaveChangesAsync();
        return RedirectToPage("/Table/Cart", new { tableId = TableId });
    }
}