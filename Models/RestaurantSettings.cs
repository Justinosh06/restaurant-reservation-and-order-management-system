using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Models
{
    public class RestaurantSettings
    {
        [Key]
        [Required(ErrorMessage = "Restaurant setting ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Restaurant name is required.")]
        [StringLength(100, ErrorMessage = "Restaurant name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Address is required.")]
        [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
        public string Address { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        public string? PhoneNumber { get; set; }

        [Required]
        public CurrencyType DefaultCurrency { get; set; } = CurrencyType.MYR;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<BusinessHours> BusinessHours { get; set; } = new List<BusinessHours>();
    }

    public class BusinessHours
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        [Required]
        public DayOfWeek DayOfWeek { get; set; } // Enum: Sunday = 0, Monday = 1, etc.

        public TimeOnly? OpenTime { get; set; }

        public TimeOnly? CloseTime { get; set; }

        [Required]
        public bool IsClosed { get; set; } = false;

        // Foreign Key
        [Required]
        public string RestaurantSettingsId { get; set; } = string.Empty;

        [ForeignKey(nameof(RestaurantSettingsId))]
        public RestaurantSettings? RestaurantSettings { get; set; }
    }
}