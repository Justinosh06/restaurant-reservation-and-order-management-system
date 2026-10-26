using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
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

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AdminClaims.CreatePrincipal(admin));

        // The dashboard ("/") is for Administrators only, so Staff start on the Orders page.
        return Redirect(admin.Role == AdminRole.Administrator ? "/" : "/Admin/Orders");
    }
}
