using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantReservation.Models
{
    public class Feedback
    {
        [Key]
        [Required(ErrorMessage = "Feedback ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        [StringLength(300, ErrorMessage = "Comment cannot exceed 300 characters.")]
        public string? Description { get; set; } = string.Empty;

        // Foreign Key
        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItemId))]
        public MenuItem? MenuItem { get; set; }

        [Required]
        public string CustomerId { get; set; } = string.Empty;

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }
    }
}