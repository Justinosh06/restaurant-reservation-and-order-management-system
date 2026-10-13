using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    public class CartItem
    {
        [Key]
        [Required(ErrorMessage = "Cart item ID is required.")]
        public string CartItemId { get; set; } = string.Empty;

        [Required]
        [Range(1, 20, ErrorMessage = "Value must be between 1 and 20.")]
        public int Quantity { get; set; }

        // Foreign Keys
        [Required]
        public string OrderId { get; set; } = string.Empty;

        public Order? Order { get; set; }

        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        public MenuItem? MenuItem { get; set; }
    }
}