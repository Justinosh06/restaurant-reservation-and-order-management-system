namespace RestaurantReservation.Infrastructure.Admin;

public static class DateRangeInput
{
    private static readonly DateOnly EarliestDate = new(2000, 1, 1);

    // Returns an error message, or null when both dates are valid.
    public static string? Check(string? from, string? to, out DateOnly fromDate, out DateOnly toDate)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out fromDate) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", out toDate))
        {
            toDate = default;
            return "Please choose a valid From and To date.";
        }
        if (fromDate < EarliestDate)
        {
            return "The From date can't be before 1 Jan 2000.";
        }
        if (toDate < fromDate)
        {
            return "The To date must be on or after the From date.";
        }
        return null;
    }
}
