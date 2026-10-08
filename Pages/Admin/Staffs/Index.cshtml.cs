using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Staffs;

public class IndexModel : PageModel
{
    public const int MinPasswordLength = 8;

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<Models.Admin> _passwordHasher;

    public IndexModel(ApplicationDbContext db, IPasswordHasher<Models.Admin> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public record SaveStaffRequest(string? Id, string? Email, string? Password, string? Role);

    public record DeleteStaffRequest(string Id);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        var staffs = await _db.Admins.AsNoTracking()
            .OrderBy(a => a.Role == AdminRole.Administrator ? 0 : 1)
            .ThenBy(a => a.Email)
            .ToListAsync();

        return new JsonResult(staffs.Select(ToDto));
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] SaveStaffRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var roleError = ValidateRole(request.Role, out var role);
        var error = ValidateEmail(email) ?? ValidatePassword(request.Password, required: true) ?? roleError;
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        if (await _db.Admins.AnyAsync(a => a.Email == email))
        {
            return BadRequest(new { error = "An account with this email already exists." });
        }

        var admin = new Models.Admin { Id = Guid.NewGuid().ToString("N"), Email = email, Role = role };
        admin.Password = _passwordHasher.HashPassword(admin, request.Password!);

        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(admin));
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] SaveStaffRequest request)
    {
        var admin = await _db.Admins.FindAsync(request.Id);
        if (admin is null)
        {
            return NotFound(new { error = "This account no longer exists. Refresh the page." });
        }

        var email = NormalizeEmail(request.Email);
        var roleError = ValidateRole(request.Role, out var role);
        var error = ValidateEmail(email) ?? ValidatePassword(request.Password, required: false) ?? roleError;
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        if (await _db.Admins.AnyAsync(a => a.Email == email && a.Id != admin.Id))
        {
            return BadRequest(new { error = "An account with this email already exists." });
        }

        if (admin.Role == AdminRole.Administrator && role != AdminRole.Administrator && await IsLastAdministratorAsync())
        {
            return BadRequest(new { error = "You can't change the role of the only Administrator." });
        }

        admin.Email = email;
        admin.Role = role;
        if (!string.IsNullOrEmpty(request.Password))
        {
            admin.Password = _passwordHasher.HashPassword(admin, request.Password);
        }

        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(admin));
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteStaffRequest request)
    {
        var admin = await _db.Admins.FindAsync(request.Id);
        if (admin is null)
        {
            return NotFound(new { error = "This account no longer exists. Refresh the page." });
        }

        if (admin.Role == AdminRole.Administrator && await IsLastAdministratorAsync())
        {
            return BadRequest(new { error = "You can't delete the only Administrator account." });
        }

        _db.Admins.Remove(admin);
        await _db.SaveChangesAsync();

        return new NoContentResult();
    }

    private static object ToDto(Models.Admin admin) => new
    {
        id = admin.Id,
        displayId = AdminDisplay.ShortId(admin.Id),
        email = admin.Email,
        role = admin.Role.ToString()
    };

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? ValidateEmail(string email) =>
        ModelRules.FirstError(new Models.Admin { Email = email }, nameof(Models.Admin.Email));

    private static string? ValidatePassword(string? password, bool required)
    {
        if (string.IsNullOrEmpty(password))
        {
            return required ? $"Password must be at least {MinPasswordLength} characters." : null;
        }
        return password.Length < MinPasswordLength ? $"Password must be at least {MinPasswordLength} characters." : null;
    }

    private static string? ValidateRole(string? value, out AdminRole role) =>
        Enum.TryParse(value, ignoreCase: false, out role) && Enum.IsDefined(role) ? null : "Please choose a valid role.";

    private async Task<bool> IsLastAdministratorAsync() =>
        await _db.Admins.CountAsync(a => a.Role == AdminRole.Administrator) <= 1;
}
