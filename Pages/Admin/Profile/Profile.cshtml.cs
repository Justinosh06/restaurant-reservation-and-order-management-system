using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;

namespace RestaurantReservation.Pages.Admin.Profile;

// The signed-in Administrator/Staff account's own picture, name and email.
[RequestFormLimits(MultipartBodyLengthLimit = ImageUploadService.MaxRequestBytes)]
public class ProfileModel : PageModel
{
    private const string UploadFolder = "uploads/admins";

    private readonly ApplicationDbContext _db;
    private readonly ImageUploadService _images;

    public ProfileModel(ApplicationDbContext db, ImageUploadService images)
    {
        _db = db;
        _images = images;
    }

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

    // A new image replaces the old one; removeImage clears it. Without either, the current image is kept.
    public async Task<IActionResult> OnPostSaveAsync(
        [FromForm] string? name,
        [FromForm] string? email,
        [FromForm] bool removeImage,
        IFormFile? image)
    {
        var admin = await CurrentAdminAsync();
        if (admin is null)
        {
            return NotFound(new { error = "This account no longer exists. Please sign in again." });
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
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

        var oldImageUrl = admin.ImageUrl;
        if (image is { Length: > 0 })
        {
            var imageError = await ImageUploadService.ValidateAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            admin.ImageUrl = await _images.SaveAsync(image, UploadFolder);
        }
        else if (removeImage)
        {
            admin.ImageUrl = null;
        }

        var emailChanged = admin.Email != normalizedEmail;
        admin.Name = trimmedName.Length == 0 ? null : trimmedName;
        admin.Email = normalizedEmail;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (admin.ImageUrl != oldImageUrl)
            {
                _images.Delete(admin.ImageUrl, UploadFolder);
            }
            throw;
        }

        if (admin.ImageUrl != oldImageUrl)
        {
            _images.Delete(oldImageUrl, UploadFolder);
        }

        // The login cookie holds the email, so it is issued again with the new one.
        if (emailChanged)
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AdminClaims.CreatePrincipal(admin));
        }

        return new JsonResult(new
        {
            name = admin.Name,
            displayName = admin.Name ?? admin.Role.ToString(),
            email = admin.Email,
            imageUrl = admin.ImageUrl
        });
    }

    private async Task<Models.Admin?> CurrentAdminAsync()
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return adminId is null ? null : await _db.Admins.FindAsync(adminId);
    }
}
