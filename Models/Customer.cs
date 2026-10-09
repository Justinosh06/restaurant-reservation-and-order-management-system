using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    public class Customer
    {
        [Key]
        [Required(ErrorMessage = "Customer ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [DataType(DataType.EmailAddress)]
        public string Email { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }
}