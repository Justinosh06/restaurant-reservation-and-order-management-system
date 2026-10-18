using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// One order from the customer's history, with its items. The customer can leave optional feedback on each item.
// Feedback is stored per customer and menu item, so sending it again for the same item updates the earlier one.
public class OrderModel : PageModel
{
    private const int MaxCommentLength = 300;

    private readonly ApplicationDbContext _db;

    public OrderModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public Order Order { get; private set; } = new();
    public IReadOnlyList<OrderItem> Items { get; private set; } = [];

    // Feedback is only for food that was actually served.
    public bool CanReview => Order.Status is OrderStatus.Served or OrderStatus.Completed;

    // Error message per menu item id.
    public Dictionary<string, string> Errors { get; } = [];

    [BindProperty]
    public List<ItemFeedbackInput> Feedback { get; set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    public class ItemFeedbackInput
    {
        public string MenuItemId { get; set; } = string.Empty;
        public int? Rating { get; set; }
        public string? Description { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (!await LoadOrderAsync(id))
        {
            return NotFound();
        }

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var menuItemIds = await ReviewableMenuItemIdsAsync();
        var existing = await _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.CustomerId == customerId && menuItemIds.Contains(f.MenuItemId))
            .ToListAsync();

        Feedback = menuItemIds
            .Select(menuItemId =>
            {
                var earlier = existing.FirstOrDefault(f => f.MenuItemId == menuItemId);
                return new ItemFeedbackInput { MenuItemId = menuItemId, Rating = earlier?.Rating, Description = earlier?.Description };
            })
            .ToList();
        return Page();
    }

    // Send feedback button
    public async Task<IActionResult> OnPostAsync(string id)
    {
        if (!await LoadOrderAsync(id))
        {
            return NotFound();
        }

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (customerId is null || !await _db.Customers.AnyAsync(c => c.Id == customerId))
        {
            return Challenge();
        }

        // Only items of this order can be reviewed, whatever the form posts.
        var menuItemIds = await ReviewableMenuItemIdsAsync();
        Feedback = menuItemIds
            .Select(menuItemId => Feedback.FirstOrDefault(f => f.MenuItemId == menuItemId)
                ?? new ItemFeedbackInput { MenuItemId = menuItemId })
            .ToList();

        foreach (var input in Feedback)
        {
            input.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

            if (input.Rating is null && input.Description is not null)
            {
                Errors[input.MenuItemId] = "Pick a star rating to go with your comment.";
            }
            else if (input.Rating is < 1 or > 5)
            {
                Errors[input.MenuItemId] = "The rating must be between 1 and 5 stars.";
            }
            else if (input.Description?.Length > MaxCommentLength)
            {
                Errors[input.MenuItemId] = $"The comment cannot be longer than {MaxCommentLength} characters.";
            }
        }

        if (Errors.Count > 0)
        {
            return Page();
        }

        var rated = Feedback.Where(f => f.Rating is not null).ToList();
        if (rated.Count == 0)
        {
            SuccessMessage = "No stars were picked, so no feedback was sent.";
            return RedirectToPage(new { id });
        }

        var existing = await _db.Feedbacks
            .Where(f => f.CustomerId == customerId && menuItemIds.Contains(f.MenuItemId))
            .ToListAsync();

        foreach (var input in rated)
        {
            var feedback = existing.FirstOrDefault(f => f.MenuItemId == input.MenuItemId);
            if (feedback is null)
            {
                // The id is set here: two new rows added with an empty id would clash before they are saved.
                feedback = new Feedback
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MenuItemId = input.MenuItemId,
                    CustomerId = customerId
                };
                _db.Feedbacks.Add(feedback);
            }

            feedback.Rating = input.Rating!.Value;
            feedback.Description = input.Description;
        }
        await _db.SaveChangesAsync();

        SuccessMessage = "Thank you! Your feedback has been sent and now shows under Reviews on the home page.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadOrderAsync(string id)
    {
        // Only the customer's own order; another customer's order is not found.
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId);
        if (order is null)
        {
            return false;
        }

        Order = order;
        Items = await _db.OrderItems
            .AsNoTracking()
            .Include(i => i.MenuItem)
            .Where(i => i.OrderId == id)
            .ToListAsync();

        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;
        return true;
    }

    // The order's menu items that still exist on the menu, in the order they appear on the page.
    private async Task<List<string>> ReviewableMenuItemIdsAsync()
    {
        if (!CanReview)
        {
            return [];
        }

        var orderedIds = Items.Select(i => i.MenuItemId).Distinct().ToList();
        var onMenu = await _db.MenuItems.Where(m => orderedIds.Contains(m.Id)).Select(m => m.Id).ToListAsync();
        return orderedIds.Where(onMenu.Contains).ToList();
    }
}
