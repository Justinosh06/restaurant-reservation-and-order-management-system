using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// One order from the customer's history, with its items. The customer can leave optional feedback on each item.
// A customer has one feedback per menu item:
// - Once given, it is locked on that order (and on any earlier order of the same item); it cannot be edited there.
// - On a later order of the same item the feedback box starts empty. Sending new feedback for that item first shows
//   the earlier feedback, and the customer chooses to replace it or keep it.
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

    // The customer's existing feedback per menu item id, from any order.
    public Dictionary<string, Feedback> Previous { get; private set; } = [];

    // Menu item ids whose feedback was given on this order or a later one: shown read-only.
    public HashSet<string> Locked { get; private set; } = [];

    // Error message per menu item id.
    public Dictionary<string, string> Errors { get; } = [];

    // Items the customer just rated that already have earlier feedback; the page asks what to do with each.
    public List<ItemFeedbackInput> Conflicts { get; private set; } = [];

    // One box per item that can still be rated on this order.
    [BindProperty]
    public List<ItemFeedbackInput> Feedback { get; set; } = [];

    // Set by the "Save my choices" button of the overwrite reminder.
    [BindProperty]
    public bool ConfirmOverwrite { get; set; }

    // The reminder's choice per menu item id: "replace" or "keep".
    [BindProperty]
    public Dictionary<string, string> OverwriteChoice { get; set; } = [];

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

        // Every open box starts empty, even when the customer reviewed the item on an earlier order.
        Feedback = (await LoadFeedbackStateAsync())
            .Select(menuItemId => new ItemFeedbackInput { MenuItemId = menuItemId })
            .ToList();
        return Page();
    }

    // Send feedback button, and the "Save my choices" button of the overwrite reminder.
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

        // Only items of this order that are still open can be rated, whatever the form posts.
        var openIds = await LoadFeedbackStateAsync();
        Feedback = openIds
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

        // New feedback for an item reviewed before: show the earlier feedback first and let the customer choose.
        var conflicts = rated.Where(f => Previous.ContainsKey(f.MenuItemId)).ToList();
        if (conflicts.Count > 0 && !ConfirmOverwrite)
        {
            Conflicts = conflicts;
            return Page();
        }

        var saved = 0;
        var kept = 0;
        foreach (var input in rated)
        {
            if (Previous.TryGetValue(input.MenuItemId, out var feedback))
            {
                // Anything but an explicit "replace" keeps the earlier feedback as it is.
                if (!OverwriteChoice.TryGetValue(input.MenuItemId, out var choice) || choice != "replace")
                {
                    kept++;
                    continue;
                }
            }
            else
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
            feedback.OrderId = Order.Id;
            saved++;
        }
        await _db.SaveChangesAsync();

        SuccessMessage = saved == 0
            ? "Your earlier feedback was kept, so nothing was changed."
            : "Thank you for your feedback!"
                + (kept > 0 ? $" Your earlier feedback was kept for {kept} {(kept == 1 ? "item" : "items")}." : "");
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

    // Loads the customer's earlier feedback on this order's items and works out which are locked.
    // Returns the menu item ids that can still be rated here, in the order they appear on the page.
    private async Task<List<string>> LoadFeedbackStateAsync()
    {
        if (!CanReview)
        {
            return [];
        }

        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var orderedIds = Items.Select(i => i.MenuItemId).Distinct().ToList();
        var onMenu = await _db.MenuItems.Where(m => orderedIds.Contains(m.Id)).Select(m => m.Id).ToListAsync();
        var reviewable = orderedIds.Where(onMenu.Contains).ToList();

        // Tracked, so a "replace" on post updates these rows.
        Previous = (await _db.Feedbacks
                .Where(f => f.CustomerId == customerId && reviewable.Contains(f.MenuItemId))
                .ToListAsync())
            .ToDictionary(f => f.MenuItemId);

        // When each piece of feedback was given, from the order it was written on.
        var feedbackOrderIds = Previous.Values.Where(f => f.OrderId != null).Select(f => f.OrderId!).Distinct().ToList();
        var orderTimes = await _db.Orders
            .Where(o => feedbackOrderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.CreatedAt);

        // Locked when the feedback came from this order or a later one. Feedback with no known order
        // (saved before orders were recorded on it) can be replaced from any order.
        Locked = Previous.Values
            .Where(f => f.OrderId != null
                && orderTimes.TryGetValue(f.OrderId, out var givenOn)
                && givenOn >= Order.CreatedAt)
            .Select(f => f.MenuItemId)
            .ToHashSet();

        return reviewable.Where(menuItemId => !Locked.Contains(menuItemId)).ToList();
    }
}
