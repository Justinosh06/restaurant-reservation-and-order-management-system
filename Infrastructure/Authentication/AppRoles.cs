namespace RestaurantReservation.Infrastructure.Authentication;

// Role claim values. Administrator and Staff match AdminRole (and wwwroot/data/admin-nav.json).
public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Staff = "Staff";
    public const string Customer = "Customer";
}
