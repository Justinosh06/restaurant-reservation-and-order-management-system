using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table;

public class CartItemViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? Remarks { get; init; }
    public string Options { get; init; } = string.Empty;
}

public class CartModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly DiningSessionService _diningSessions;
    private readonly RestaurantCurrency _restaurantCurrency;
    private readonly RestaurantOrderingHours _orderingHours;

    public CartModel(
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

    public string TableId { get; private set; } = string.Empty;
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public string CurrencySymbol => Currency.GetSymbol();
    public List<CartItemViewModel> CartItems { get; private set; } = [];
    public List<RestaurantReservation.Models.MenuItem> Recommendations { get; private set; } = [];
    public decimal Subtotal => CartItems.Sum(item => item.UnitPrice * item.Quantity);

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string tableId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        await LoadCartAsync(session.Session!);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateQuantityAsync(
        string tableId,
        string cartItemId,
        int quantityDelta)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        var cartItem = await _db.CartItems.FirstOrDefaultAsync(item =>
            item.Id == cartItemId && item.SessionId == session.Session!.Id);
        if (cartItem is null)
        {
            return NotFound();
        }

        if (quantityDelta is not (-1 or 1))
        {
            return BadRequest();
        }

        cartItem.Quantity = Math.Clamp(cartItem.Quantity + quantityDelta, 1, 20);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Table/Cart", new { tableId = TableId });
    }

    public async Task<IActionResult> OnPostRemoveAsync(string tableId, string cartItemId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        var cartItem = await _db.CartItems.FirstOrDefaultAsync(item =>
            item.Id == cartItemId && item.SessionId == session.Session!.Id);
        if (cartItem is null)
        {
            return NotFound();
        }

        _db.CartItems.Remove(cartItem);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Table/Cart", new { tableId = TableId });
    }

    public async Task<IActionResult> OnPostPlaceOrderAsync(string tableId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        var cartItems = await _db.CartItems
            .Include(item => item.MenuItem)
            .Where(item => item.SessionId == session.Session!.Id)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            StatusMessage = "Your cart is empty.";
            return RedirectToPage("/Table/Cart", new { tableId = TableId });
        }

        if (cartItems.Any(item => item.MenuItem is null || !item.MenuItem.Availability))
        {
            StatusMessage = "One or more items are no longer available. Remove them before placing your order.";
            await LoadCartAsync(session.Session!);
            return Page();
        }

        if (await _orderingHours.IsOrderingClosedAsync())
        {
            return RedirectToPage("/Table/Index", new { tableId = TableId });
        }

        var totalAmount = cartItems.Sum(item => (long)item.MenuItem!.Price * item.Quantity);
        if (totalAmount > 10_000_000)
        {
            StatusMessage = "The cart total exceeds the maximum order amount.";
            await LoadCartAsync(session.Session!);
            return Page();
        }

        var order = new Order
        {
            SessionId = session.Session!.Id,
            CustomerId = session.Session.CustomerId,
            TotalAmount = (int)totalAmount,
            Status = OrderStatus.InQueue,
            CreatedAt = DateTime.UtcNow,
            OrderItems = cartItems
                .Select(item => new OrderItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MenuItemId = item.MenuItemId,
                    Quantity = item.Quantity,
                    UnitPrice = item.MenuItem!.Price,
                    ItemName = item.MenuItem.Name,
                    Remarks = item.Remarks,
                    Options = new Dictionary<string, string>(item.Options, StringComparer.Ordinal)
                })
                .ToList()
        };

        _db.Orders.Add(order);
        _db.CartItems.RemoveRange(cartItems);
        await _db.SaveChangesAsync();

        StatusMessage = "Your order has been placed.";
        return RedirectToPage("/Table/Checkout", new { tableId = TableId });
    }

    private async Task<(Session? Session, IActionResult? Result)> GetActiveSessionAsync(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return (null, NotFound());
        }

        TableId = tableId.ToUpperInvariant();
        var session = await _diningSessions.GetActiveSessionAsync(TableId);
        if (session is null)
        {
            return (null, RedirectToPage("/Table/Index", new { tableId = TableId }));
        }

        if (await _orderingHours.IsOrderingClosedAsync())
        {
            return (null, RedirectToPage("/Table/Index", new { tableId = TableId }));
        }

        return (session, null);
    }

    private async Task LoadCartAsync(Session session)
    {
        Currency = await _restaurantCurrency.GetAsync();
        var cartItems = await _db.CartItems
            .AsNoTracking()
            .Where(item => item.SessionId == session.Id)
            .Include(item => item.MenuItem)
            .OrderBy(item => item.Id)
            .ToListAsync();

        CartItems = cartItems.Select(item => new CartItemViewModel
            {
                Id = item.Id,
                Name = item.MenuItem?.Name ?? "Unavailable menu item",
                UnitPrice = (item.MenuItem?.Price ?? 0) / 100m,
                Quantity = item.Quantity,
                Type = item.MenuItem?.MenuItemType.ToString() ?? string.Empty,
                ImageUrl = item.MenuItem?.ThumbnailImage,
                Remarks = item.Remarks,
                Options = string.Join(", ", item.Options.Select(option => $"{option.Key}: {option.Value}"))
            })
            .ToList();

        var cartMenuItemIds = await _db.CartItems
            .Where(item => item.SessionId == session.Id)
            .Select(item => item.MenuItemId)
            .Distinct()
            .ToListAsync();
        var ratings = _db.Feedbacks
            .AsNoTracking()
            .GroupBy(feedback => feedback.MenuItemId)
            .Select(group => new
            {
                MenuItemId = group.Key,
                AverageRating = group.Average(feedback => feedback.Rating),
                RatingCount = group.Count()
            });

        Recommendations = await (
            from item in _db.MenuItems.AsNoTracking()
            join rating in ratings on item.Id equals rating.MenuItemId into itemRatings
            from rating in itemRatings.DefaultIfEmpty()
            where item.Availability && !cartMenuItemIds.Contains(item.Id)
            orderby (double?)rating.AverageRating descending,
                (int?)rating.RatingCount descending,
                item.Name,
                item.Id
            select item)
            .Take(6)
            .ToListAsync();

        ViewData["CartItemCount"] = CartItems.Sum(item => item.Quantity);
    }
}
