using RestaurantReservation.Models;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer.Dining;

// One dining session (a visit to the restaurant) and the customer's orders placed during it.
public record DiningVisit(Session Session, IReadOnlyList<Order> Orders)
{
    public int TotalAmount => Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount);
}
