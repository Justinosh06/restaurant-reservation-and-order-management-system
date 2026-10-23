using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Reviews;

// The options a reviewed dish was ordered with (Iced or Hot, Regular or Large, ...), shown beside the dish name in
// reviews. They are not stored on the feedback: they come from the order the feedback was written on. One order can
// have the same dish twice with different options (an Iced and a Hot Matcha Latte), so a review can have several.
public static class ReviewOptions
{
    // Each review's option sets by feedback id, without repeats. Reviews with no known order, or a dish ordered
    // without options, have none.
    public static async Task<Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>> LoadAsync(
        ApplicationDbContext db, IEnumerable<Feedback> reviews)
    {
        var withOrder = reviews.Where(f => f.OrderId != null).ToList();
        var orderIds = withOrder.Select(f => f.OrderId!).Distinct().ToList();

        var items = await db.OrderItems
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.MenuItemId, i.Options })
            .ToListAsync();

        return withOrder.ToDictionary(
            f => f.Id,
            f => (IReadOnlyList<IReadOnlyDictionary<string, string>>)items
                .Where(i => i.OrderId == f.OrderId && i.MenuItemId == f.MenuItemId && i.Options.Count > 0)
                .Select(i => i.Options)
                .DistinctBy(Key)
                .Select(options => (IReadOnlyDictionary<string, string>)options)
                .ToList());
    }

    // "Temperature: Iced", the same wording as the cart and checkout.
    public static IEnumerable<string> Labels(IReadOnlyDictionary<string, string> options) =>
        options.Select(option => $"{option.Key}: {option.Value}");

    private static string Key(Dictionary<string, string> options) =>
        string.Join("|", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));
}
