using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Announcements;

public class AnnouncementModel : PageModel
{
    public static readonly IReadOnlyList<(string Name, string Icon)> Categories =
    [
        ("General", "fa-solid fa-circle-info"),
        ("Closure", "fa-solid fa-door-closed"),
        ("Hours change", "fa-solid fa-clock"),
        ("Maintenance", "fa-solid fa-screwdriver-wrench"),
        ("New menu", "fa-solid fa-utensils"),
        ("Event", "fa-solid fa-champagne-glasses"),
        ("Festive", "fa-solid fa-gift"),
        ("Urgent", "fa-solid fa-triangle-exclamation")
    ];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public AnnouncementModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => _clock.Today;

    public record SaveAnnouncementRequest(string? Id, string? Title, string? Description, string? Category, bool IsActive, string? ExpiresAt);

    public record AnnouncementIdRequest(string Id);

    public async Task<IActionResult> OnGetListAsync()
    {
        var announcements = await _db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return new JsonResult(announcements.Select(ToDto));
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] SaveAnnouncementRequest request)
    {
        var announcement = new Announcement();
        var error = Apply(request, announcement);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(announcement));
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] SaveAnnouncementRequest request)
    {
        var announcement = await _db.Announcements.FindAsync(request.Id);
        if (announcement is null)
        {
            return NotFound(new { error = "This announcement no longer exists. Refresh the page." });
        }

        var error = Apply(request, announcement);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(announcement));
    }

    // Copies the form values onto the announcement. Returns an error message, or null when everything is valid.
    private string? Apply(SaveAnnouncementRequest request, Announcement announcement)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var error = ModelRules.FirstError(new Announcement { Title = title, Description = description },
            nameof(Announcement.Title), nameof(Announcement.Description));
        if (error is not null)
        {
            return error;
        }
        if (!Categories.Any(c => c.Name == request.Category))
        {
            return "Please choose one of the listed categories.";
        }

        DateTime? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(request.ExpiresAt))
        {
            if (!DateOnly.TryParseExact(request.ExpiresAt, "yyyy-MM-dd", out var lastDay))
            {
                return "Please enter a valid date.";
            }
            expiresAt = _clock.StartOfDayUtc(lastDay.AddDays(1));

            // A past date is only allowed when it is the date the announcement already had.
            if (lastDay < _clock.Today && expiresAt != announcement.ExpiresAt)
            {
                return "The \"Show until\" date can't be in the past.";
            }
        }

        announcement.Title = title;
        announcement.Description = description;
        announcement.Category = request.Category!;
        announcement.IsActive = request.IsActive;
        announcement.ExpiresAt = expiresAt;
        return null;
    }

    public async Task<IActionResult> OnPostToggleAsync([FromBody] AnnouncementIdRequest request)
    {
        var announcement = await _db.Announcements.FindAsync(request.Id);
        if (announcement is null)
        {
            return NotFound(new { error = "This announcement no longer exists. Refresh the page." });
        }

        announcement.IsActive = !announcement.IsActive;
        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(announcement));
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] AnnouncementIdRequest request)
    {
        var announcement = await _db.Announcements.FindAsync(request.Id);
        if (announcement is null)
        {
            return NotFound(new { error = "This announcement no longer exists. Refresh the page." });
        }

        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync();

        return new NoContentResult();
    }

    private object ToDto(Announcement a) => new
    {
        id = a.Id,
        title = a.Title,
        description = a.Description,
        category = a.Category,
        icon = Categories.FirstOrDefault(c => c.Name == a.Category).Icon ?? Categories[0].Icon,
        isActive = a.IsActive,
        isExpired = a.ExpiresAt <= DateTime.UtcNow,
        createdAt = a.CreatedAt,
        lastDay = a.ExpiresAt.HasValue
            ? DateOnly.FromDateTime(_clock.ToLocal(a.ExpiresAt.Value).AddTicks(-1)).ToString("yyyy-MM-dd")
            : null
    };
}
