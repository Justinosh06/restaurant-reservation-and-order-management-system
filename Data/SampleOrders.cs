using RestaurantReservation.Models;

namespace RestaurantReservation.Data;

// One dining session (a visit to the restaurant) and the orders placed during it.
public record DiningVisit(Session Session, IReadOnlyList<Order> Orders)
{
    public int TotalAmount => Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount);
}

// Mock dining and order history, shaped like the orders on the checkout page: the order code is the order time (yyMMddHHmm).
// Stands in for the Sessions and Orders tables until order processing saves real ones.
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

    // Newest visit first.
    public static IReadOnlyList<DiningVisit> Visits { get; }
    public static IReadOnlyList<Order> All { get; }
    public static ILookup<string, OrderItem> Items { get; }

    static SampleOrders()
    {
        var items = new List<OrderItem>();

        // Times are given in local time and stored in UTC, like the real tables.
        DiningVisit Visit(DateTime arrived, int minutesAtTable, params Order[] orders)
        {
            var start = arrived.ToUniversalTime();
            var session = new Session
            {
                Id = "S" + arrived.ToString("yyMMddHHmm"),
                StartingTime = start,
                EndingTime = start.AddMinutes(minutesAtTable)
            };
            foreach (var order in orders)
            {
                order.SessionId = session.Id;
                order.Session = session;
            }
            return new DiningVisit(session, orders);
        }

        Order Sample(DateTime orderedAt, OrderStatus status, params (string MenuItemId, int Quantity, string? Remarks)[] lines)
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

            return new Order
            {
                Id = id,
                Status = status,
                CreatedAt = orderedAt.ToUniversalTime(),
                TotalAmount = lines.Sum(l => Menu[l.MenuItemId].Price * l.Quantity)
            };
        }

        Visits =
        [
            Visit(new DateTime(2026, 10, 7, 19, 56, 0), 75,
                Sample(new DateTime(2026, 10, 7, 20, 6, 0), OrderStatus.Completed,
                    ("PAST-01", 2, "Extra cheese"), ("BEV-01", 1, null)),
                Sample(new DateTime(2026, 10, 7, 20, 45, 0), OrderStatus.Completed,
                    ("BEV-02", 1, null))),
            Visit(new DateTime(2026, 9, 28, 12, 20, 0), 55,
                Sample(new DateTime(2026, 9, 28, 12, 30, 0), OrderStatus.Completed,
                    ("PAST-02", 1, null), ("BEV-03", 2, "Less ice"))),
            Visit(new DateTime(2026, 9, 14, 19, 5, 0), 20,
                Sample(new DateTime(2026, 9, 14, 19, 15, 0), OrderStatus.Cancelled,
                    ("PAST-03", 1, null), ("BEV-02", 1, null))),
            Visit(new DateTime(2026, 8, 30, 13, 35, 0), 60,
                Sample(new DateTime(2026, 8, 30, 13, 45, 0), OrderStatus.Completed,
                    ("PAST-02", 2, null), ("BEV-02", 2, null)),
                Sample(new DateTime(2026, 8, 30, 14, 10, 0), OrderStatus.Completed,
                    ("PAST-03", 1, null), ("BEV-01", 1, null)))
        ];
        All = Visits.SelectMany(v => v.Orders).ToList();
        Items = items.ToLookup(i => i.OrderId);
    }

    public static Order? Find(string id) => All.FirstOrDefault(o => o.Id == id);

    public static DiningVisit? FindVisit(string id) => Visits.FirstOrDefault(v => v.Session.Id == id);
}
