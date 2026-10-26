using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models
{
    // A dated copy of the weekly opening hours, one row per weekday, saved by Restaurant Settings each time the
    // hours change. Table utilization uses the hours that were in force on each past date, so changing the hours
    // today doesn't change how last month is measured. EffectiveFrom = DateOnly.MinValue is the schedule from
    // before the first change was recorded.
    public class OpeningHoursHistory
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        [Required]
        public DateOnly EffectiveFrom { get; set; }

        [Required]
        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly? OpenTime { get; set; }

        public TimeOnly? CloseTime { get; set; }

        [Required]
        public bool IsClosed { get; set; }
    }
}
