using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantReservation.Models
{
    public class Promotion
    {
        [Key]
        [Required(ErrorMessage = "Promotion ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required]
        public DateOnly StartDate { get; set; }

        [Required]
        public DateOnly EndDate { get; set; }

        // Foreign Key
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }
    }
}