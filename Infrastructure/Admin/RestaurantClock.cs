namespace RestaurantReservation.Infrastructure.Admin;

public class RestaurantClock
{
    private const string DefaultTimeZone = "Asia/Kuala_Lumpur";

    private readonly TimeZoneInfo _timeZone;

    public RestaurantClock(IConfiguration configuration)
    {
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Restaurant:TimeZone"] ?? DefaultTimeZone);
    }

    public DateTime Now => ToLocal(DateTime.UtcNow);

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _timeZone);

    public DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _timeZone);
}
