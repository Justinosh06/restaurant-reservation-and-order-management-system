using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    public class CartItem
    {
        [Key]
        [Required(ErrorMessage = "Cart item ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required]
        [Range(1, 20, ErrorMessage = "Value must be between 1 and 20.")]
        public int Quantity { get; set; }

        public Dictionary<string, string> Options { get; set; } = new();

        // Foreign Keys
        [Required]
        public string SessionId { get; set; } = string.Empty;

        public Session? Session { get; set; }

        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        public MenuItem? MenuItem { get; set; }
    }
}