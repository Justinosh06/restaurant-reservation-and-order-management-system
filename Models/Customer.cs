using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RestaurantReservation.Models.Enums;

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

        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}".Trim();

        [StringLength(20)]
        [DataType(DataType.PhoneNumber)]
        public string? PhoneNumber { get; set; }

        // Optional details the customer can add on the profile page.
        public Gender? Gender { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? DateOfBirth { get; set; }

        // When the customer last opened the announcement bell; newer announcements count as unread.
        public DateTime? AnnouncementsSeenAt { get; set; }

        // Stores a hashed password (ASP.NET Core PasswordHasher), never plain text.
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }
}