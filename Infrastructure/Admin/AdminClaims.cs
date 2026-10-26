using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RestaurantReservation.Infrastructure.Admin;

public static class AdminClaims
{
    // The login cookie for an Administrator/Staff account: issued when they sign in (Pages/Admin/SignIn.cshtml.cs)
    // and again after a profile or account change (Profile and Staffs pages).
    public static ClaimsPrincipal CreatePrincipal(Models.Admin admin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id),
            new(ClaimTypes.Name, admin.Email),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Role, admin.Role.ToString())
        };
        if (admin.Gender.HasValue)
        {
            claims.Add(new Claim("gender", admin.Gender.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
