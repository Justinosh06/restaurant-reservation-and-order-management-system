using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

// admin.localhost/Admin/SignIn?ticket=... - finishes an Admin/Staff login started on the main /Login page.
public class SignInModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly AdminSignInTicketService _tickets;

    public SignInModel(ApplicationDbContext db, AdminSignInTicketService tickets)
    {
        _db = db;
        _tickets = tickets;
    }

    public string LoginUrl { get; private set; } = "/Login";

    public async Task<IActionResult> OnGetAsync(string? ticket)
    {
        var adminId = _tickets.Redeem(ticket);
        var admin = adminId is null ? null : await _db.Admins.FindAsync(adminId);
        if (admin is null)
        {
            LoginUrl = SiteHosts.MainSiteUrl(Request, "/Login");
            return Page();
        }

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

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // The dashboard ("/") is for Administrators only, so Staff start on the Orders page.
        return Redirect(admin.Role == AdminRole.Administrator ? "/" : "/Admin/Orders");
    }
}
