using System.ComponentModel.DataAnnotations;
using RestaurantReservation.Models.Enums;

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

        // Nullable so accounts created before this field existed still load; the Staffs page requires it.
        public Gender? Gender { get; set; }

        // Set on the Profile page. Without a name the sidebar shows the role (with the email under it).
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string? Name { get; set; }

        // When this account last opened the announcement notifications; newer announcements count as unread.
        public DateTime? AnnouncementsSeenAt { get; set; }
    }
}