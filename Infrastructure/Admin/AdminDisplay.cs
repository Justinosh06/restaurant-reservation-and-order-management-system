using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Admin;

public static class AdminDisplay
{
    public static string ShortId(string id) =>
        (id.Length > 8 ? id[..8] : id).ToUpperInvariant();

    public static string OrderStatusLabel(OrderStatus status) =>
        status == OrderStatus.InQueue ? "Pending" : status.ToString();
}
