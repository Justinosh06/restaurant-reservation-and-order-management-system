using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantReservation.Models
{
    public class OrderItem
    {
        [Key]
        [Required(ErrorMessage = "Order item ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required]
        [Range(1, 20)]
        public int Quantity { get; set; }

        public string? Remarks { get; set; }

        // Foreign Keys
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }

        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItemId))]
        public MenuItem? MenuItem { get; set; }
    }
}