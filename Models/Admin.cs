using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    public enum AdminRole
    {
        Staff,
        Administrator
    }

    public class Admin
    {
        [Key]
        [Required(ErrorMessage = "Admin ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        [DataType(DataType.EmailAddress)]
        public string Email { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Required]
        public AdminRole Role { get; set; } = AdminRole.Staff;
    }
}