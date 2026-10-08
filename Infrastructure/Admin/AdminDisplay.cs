using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Admin;

public static class AdminDisplay
{
    public static string ShortId(string id) =>
        (id.Length > 8 ? id[..8] : id).ToUpperInvariant();

    public static string OrderStatus(OrderStatus status) => status switch
    {
        Models.OrderStatus.InQueue => "Pending",
        _ => status.ToString()
    };
}
