using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RestaurantReservation.Data
{
    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
        {
        }
    }
}
