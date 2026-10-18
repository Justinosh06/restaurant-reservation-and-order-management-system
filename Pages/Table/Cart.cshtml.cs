using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Pages.Table;

public class CartItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public int Quantity { get; set; }
    public MenuItemType Type { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
}

public class CartModel : PageModel
{
    public string TableId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = "OT012610072209";
    public List<CartItemViewModel> CartItems { get; set; } = new();

    public IActionResult OnGet(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return NotFound();
        }

        TableId = tableId;

        // Mock Cart Data
        CartItems = new List<CartItemViewModel>
        {
            new CartItemViewModel 
            { 
                Id = "SPAG-01", 
                Name = "Spaghetti Bolognaise", 
                UnitPrice = 24.90, 
                Quantity = 2, 
                Type = MenuItemType.Pasta, 
                ImageUrl = "/image/Menu/matcha.png",
                Remark = "Extra cheese, less spicy"
            },
            new CartItemViewModel 
            { 
                Id = "BEV-01", 
                Name = "Iced Cappuccino", 
                UnitPrice = 12.90, 
                Quantity = 1, 
                Type = MenuItemType.Beverage, 
                ImageUrl = "/image/Menu/matcha.png",
                Remark = "Less ice"
            }
        };

        return Page();
    }
}