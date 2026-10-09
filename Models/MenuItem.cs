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

        [Required]
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

        [Required]
        public bool Availability { get; set; } = true;
    }
}