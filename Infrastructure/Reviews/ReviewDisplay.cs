using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Reviews;

// How a reviewer is shown on the public review pages (landing page and /Reviews): first name and the first
// letter of the last name ("Mei T."), never the full name or email, plus the letter for their round avatar.
public static class ReviewDisplay
{
    public static string Name(Customer? customer)
    {
        var first = customer?.FirstName?.Trim();
        if (string.IsNullOrEmpty(first))
        {
            return "Guest";
        }

        var last = customer!.LastName?.Trim();
        return string.IsNullOrEmpty(last) ? first : $"{first} {char.ToUpperInvariant(last[0])}.";
    }

    public static string Initial(Customer? customer) => Name(customer)[..1].ToUpperInvariant();

    // Review dates are stored in UTC.
    public static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
