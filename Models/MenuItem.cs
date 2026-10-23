using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Models
{
    public class MenuItem
    {
        [Key]
        [Required(ErrorMessage = "Menu item ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; } = string.Empty;

        [Required]
        public MenuItemType MenuItemType { get; set; }

        [Required]
        [Range(0, 10000000, ErrorMessage = "Price must be positive.")]
        public int Price { get; set; }

        [NotMapped]
        public decimal PriceInMainUnit => Price / 100m;

        public string? ThumbnailImage { get; set; }

        public Dictionary<string, string> Options { get; set; } = new();

        [Required]
        public bool Availability { get; set; } = true;

        // When the dish was added to the menu (UTC). Set automatically for new dishes; the newest ones are the
        // Chef's specials on the home page. Dishes from before this column existed all share one date.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}