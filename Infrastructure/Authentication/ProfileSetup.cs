using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using CustomerAccount = RestaurantReservation.Models.Customer;

namespace RestaurantReservation.Infrastructure.Authentication;

// Customers must have a first name, last name and phone number before they use the site. Accounts made by a
// login code or a guest checkout start with only an email, so those customers are sent to /Customer/Setup first.
// The login cookie carries a "profile_complete" claim so the database is only checked while it is not "true".
public static class ProfileSetup
{
    public const string ClaimType = "profile_complete";
    public const string SetupPath = "/Customer/Setup";

    // Pages a customer can still open before finishing the setup.
    private static readonly string[] OpenPaths = [SetupPath, "/Logout", "/Error", "/NotFound", "/AccessDenied", "/api"];

    public static bool IsComplete(CustomerAccount customer) =>
        !string.IsNullOrWhiteSpace(customer.FirstName)
        && !string.IsNullOrWhiteSpace(customer.LastName)
        && !string.IsNullOrWhiteSpace(customer.PhoneNumber);

    // The display name for the login cookie: never the email address, which would show as "Hi, name@gmail.com".
    public static string DisplayName(CustomerAccount customer) =>
        string.IsNullOrWhiteSpace(customer.FullName) ? "there" : customer.FullName;

    public static List<Claim> CustomerClaims(CustomerAccount customer) =>
    [
        new(ClaimTypes.NameIdentifier, customer.Id),
        new(ClaimTypes.Name, DisplayName(customer)),
        new(ClaimTypes.Email, customer.Email),
        new(ClaimTypes.Role, AppRoles.Customer),
        new(ClaimType, IsComplete(customer) ? "true" : "false")
    ];

    public static Task SignInCustomerAsync(HttpContext context, CustomerAccount customer)
    {
        var identity = new ClaimsIdentity(CustomerClaims(customer), CookieAuthenticationDefaults.AuthenticationScheme);
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    // Runs after authentication: sends a customer without a complete profile to the setup page.
    public static IApplicationBuilder UseProfileSetup(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var user = context.User;
            if (!user.IsInRole(AppRoles.Customer)
                || user.FindFirstValue(ClaimType) == "true"
                || OpenPaths.Any(path => context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            // Not known to be complete (a new account, or a cookie from before this check): look it up.
            var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == user.FindFirstValue(ClaimTypes.NameIdentifier));
            if (customer is not null && IsComplete(customer))
            {
                // Remember it in the cookie so the database is not asked again.
                await SignInCustomerAsync(context, customer);
                await next();
                return;
            }

            var returnUrl = context.Request.Method == HttpMethods.Get
                ? context.Request.PathBase + context.Request.Path + context.Request.QueryString
                : "/";
            context.Response.Redirect($"{SetupPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        });
}
