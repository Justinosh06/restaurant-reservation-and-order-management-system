using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    public enum TableStatus
    {
        Empty,
        Reserved,
        Occupied    
    }

    public class Table
    {
        [Key]
        [Required(ErrorMessage = "Table ID is required.")]
        [StringLength(20, MinimumLength = 1, ErrorMessage = "Table ID must be between 1 and 20 characters.")]
        public string Id { get; set; } = string.Empty;

        [Range(1, 50, ErrorMessage = "Capacity must be between 1 and 50.")]
        public int Capacity { get; set; }

        public int CoordinateX { get; set; }
        public int CoordinateY { get; set; }

        [Required]
        [StringLength(20)]
        public TableStatus Status { get; set; } = TableStatus.Empty;
    }
}