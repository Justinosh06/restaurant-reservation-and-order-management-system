using RestaurantReservation.Models;

namespace RestaurantReservation.Data;

// Mock order history, shaped like the orders on the checkout page: the order code is the order time (yyMMddHHmm).
// Stands in for the Orders table until order processing saves real orders.
// The menu items are the ones added by DbSeeder, so feedback on them can be stored.
public static class SampleOrders
{
    private static readonly Dictionary<string, MenuItem> Menu = new MenuItem[]
    {
        new() { Id = "PAST-01", Name = "Penne Bolognaise", Price = 2490, ThumbnailImage = "/images/menu/penne-bolognaise.jpg" },
        new() { Id = "PAST-02", Name = "Creamy Carbonara", Price = 2250, ThumbnailImage = "/images/menu/creamy-carbonara.jpg" },
        new() { Id = "PAST-03", Name = "Pesto Farfalle", Price = 1990, ThumbnailImage = "/images/menu/pesto-farfalle.jpg" },
        new() { Id = "BEV-01", Name = "Iced Cappuccino", Price = 1290, ThumbnailImage = "/images/menu/iced-cappuccino.jpg" },
        new() { Id = "BEV-02", Name = "Matcha Latte", Price = 1400, ThumbnailImage = "/images/menu/matcha-latte.jpg" },
        new() { Id = "BEV-03", Name = "Fresh Lemonade", Price = 890, ThumbnailImage = "/images/menu/fresh-lemonade.jpg" }
    }.ToDictionary(m => m.Id);

    public static IReadOnlyList<Order> All { get; }
    public static ILookup<string, OrderItem> Items { get; }

    static SampleOrders()
    {
        var items = new List<OrderItem>();

        Order Sample(DateTime orderedAt, int minutesAtTable, OrderStatus status,
            params (string MenuItemId, int Quantity, string? Remarks)[] lines)
        {
            var id = orderedAt.ToString("yyMMddHHmm");
            items.AddRange(lines.Select(l => new OrderItem
            {
                OrderId = id,
                MenuItemId = l.MenuItemId,
                MenuItem = Menu[l.MenuItemId],
                Quantity = l.Quantity,
                Remarks = l.Remarks
            }));

            // Times are stored in UTC; the customer sat down ten minutes before ordering.
            var arrived = orderedAt.ToUniversalTime().AddMinutes(-10);
            return new Order
            {
                Id = id,
                Status = status,
                CreatedAt = orderedAt.ToUniversalTime(),
                TotalAmount = lines.Sum(l => Menu[l.MenuItemId].Price * l.Quantity),
                Session = new Session { StartingTime = arrived, EndingTime = arrived.AddMinutes(minutesAtTable) }
            };
        }

        All =
        [
            Sample(new DateTime(2026, 10, 7, 20, 6, 0), 75, OrderStatus.Completed,
                ("PAST-01", 2, "Extra cheese"), ("BEV-01", 1, null)),
            Sample(new DateTime(2026, 9, 28, 12, 30, 0), 55, OrderStatus.Completed,
                ("PAST-02", 1, null), ("BEV-03", 2, "Less ice")),
            Sample(new DateTime(2026, 9, 14, 19, 15, 0), 20, OrderStatus.Cancelled,
                ("PAST-03", 1, null), ("BEV-02", 1, null)),
            Sample(new DateTime(2026, 8, 30, 13, 45, 0), 60, OrderStatus.Completed,
                ("PAST-02", 2, null), ("BEV-02", 2, null))
        ];
        Items = items.ToLookup(i => i.OrderId);
    }

    public static Order? Find(string id) => All.FirstOrDefault(o => o.Id == id);
}
