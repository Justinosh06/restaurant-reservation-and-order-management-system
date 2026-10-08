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

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(250)]
        public string Address { get; set; } = string.Empty;

        [StringLength(20)]
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