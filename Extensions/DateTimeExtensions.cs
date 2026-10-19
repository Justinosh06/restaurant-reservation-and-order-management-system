namespace RestaurantReservation.Extensions
{
    // Shows any stored time (UTC) in the restaurant's local time (Malaysia), e.g. payment, order or reservation times.
    //   transaction.CreatedAt.ToDisplayTime()   // "10 Oct, 2026 15:33"
    //   order.ServedAt.ToDisplayTime()          // "-" when there's no time yet
    public static class DateTimeExtensions
    {
        public const string DisplayFormat = "dd MMM, yyyy HH:mm";

        private static readonly TimeZoneInfo RestaurantTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

        // Converts a UTC time to the restaurant's local time.
        // Times read back from the database come out as Unspecified and are treated as UTC.
        public static DateTime ToRestaurantTime(this DateTime utcTime)
        {
            var utc = utcTime.Kind == DateTimeKind.Local
                ? utcTime.ToUniversalTime()
                : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utc, RestaurantTimeZone);
        }

        // Formats a UTC time for display in the restaurant's local time, e.g. "10 Oct, 2026 15:33".
        public static string ToDisplayTime(this DateTime utcTime) => utcTime.ToRestaurantTime().ToString(DisplayFormat);

        // Same as above, but shows emptyText when there's no time (e.g. not paid or not served yet).
        public static string ToDisplayTime(this DateTime? utcTime, string emptyText = "-") =>
            utcTime.HasValue ? utcTime.Value.ToDisplayTime() : emptyText;
    }
}
