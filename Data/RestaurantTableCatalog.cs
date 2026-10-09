using Restaurant_Reservation_and_Order_Management_System.Models;

namespace Restaurant_Reservation_and_Order_Management_System.Data;

public static class RestaurantTableCatalog
{
    public static List<RestaurantTable> GetAll() =>
    [
        new() { Id = 1, Capacity = 2 },
        new() { Id = 2, Capacity = 4 },
        new() { Id = 3, Capacity = 4 },
        new() { Id = 4, Capacity = 4 },
        new() { Id = 5, Capacity = 2 },
        new() { Id = 6, Capacity = 6 },
        new() { Id = 7, Capacity = 4 },
        new() { Id = 8, Capacity = 4 },
        new() { Id = 9, Capacity = 4 },
        new() { Id = 10, Capacity = 4 },
        new() { Id = 11, Capacity = 4 },
        new() { Id = 12, Capacity = 4 },
        new() { Id = 13, Capacity = 4 },
        new() { Id = 14, Capacity = 4 },
        new() { Id = 15, Capacity = 4 },
        new() { Id = 16, Capacity = 6 }
    ];
}
