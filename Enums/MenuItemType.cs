using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models.Enums
{
    // Listed in the order a meal is served; the menu pages show their category tabs in this order.
    // Stored in the database as the name (e.g. "Pasta"), so the order can change without touching saved items.
    public enum MenuItemType
    {
        Starter,
        Salad,
        Pasta,
        Pizza,
        Main,
        Dessert,
        Beverage
    }
}
