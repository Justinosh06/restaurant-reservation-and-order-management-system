using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;

namespace RestaurantReservation.Pages.Admin.Profile;

// The signed-in Administrator/Staff account's own name and email. The avatar is generated from the gender.
public class ProfileModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ProfileModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public record SaveProfileRequest(string? Name, string? Email);

    public Models.Admin Account { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var admin = await CurrentAdminAsync();
        if (admin is null)
        {
            return NotFound();
        }

        Account = admin;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync([FromBody] SaveProfileRequest request)
    {
        var admin = await CurrentAdminAsync();
        if (admin is null)
        {
            return NotFound(new { error = "This account no longer exists. Please sign in again." });
        }

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var error = ModelRules.FirstError(new Models.Admin { Name = trimmedName, Email = normalizedEmail },
            nameof(Models.Admin.Name), nameof(Models.Admin.Email));
        if (error is not null)
        {
            return BadRequest(new { error });
        }
        if (await _db.Admins.AnyAsync(a => a.Email == normalizedEmail && a.Id != admin.Id))
        {
            return BadRequest(new { error = "Another account already uses this email." });
        }

        var emailChanged = admin.Email != normalizedEmail;
        admin.Name = trimmedName.Length == 0 ? null : trimmedName;
        admin.Email = normalizedEmail;

        await _db.SaveChangesAsync();

        // The login cookie holds the email, so it is issued again with the new one.
        if (emailChanged)
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AdminClaims.CreatePrincipal(admin));
        }

        return new JsonResult(new
        {
            name = admin.Name,
            displayName = admin.Name ?? admin.Role.ToString(),
            email = admin.Email
        });
    }

    private async Task<Models.Admin?> CurrentAdminAsync()
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return adminId is null ? null : await _db.Admins.FindAsync(adminId);
    }
}
