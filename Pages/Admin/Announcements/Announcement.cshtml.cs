using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Announcements;

public class AnnouncementModel : PageModel
{
    public static readonly IReadOnlyList<(string Value, string Label)> Icons =
    [
        ("fa-solid fa-circle-info", "General"),
        ("fa-solid fa-door-closed", "Closure"),
        ("fa-solid fa-clock", "Hours change"),
        ("fa-solid fa-screwdriver-wrench", "Maintenance"),
        ("fa-solid fa-utensils", "New menu"),
        ("fa-solid fa-champagne-glasses", "Event"),
        ("fa-solid fa-gift", "Festive"),
        ("fa-solid fa-triangle-exclamation", "Urgent")
    ];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public AnnouncementModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => _clock.Today;

    public record CreateAnnouncementRequest(string? Title, string? Description, string? Icon, bool IsActive, string? ExpiresAt);

    public record AnnouncementIdRequest(string Id);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        var announcements = await _db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return new JsonResult(announcements.Select(ToDto));
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] CreateAnnouncementRequest request)
    {
        var announcement = new Announcement
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            Icon = request.Icon ?? string.Empty,
            IsActive = request.IsActive
        };

        var error = ModelRules.FirstError(announcement, nameof(Announcement.Title), nameof(Announcement.Description));
        if (error is not null)
        {
            return BadRequest(new { error });
        }
        if (!Icons.Any(i => i.Value == announcement.Icon))
        {
            return BadRequest(new { error = "Please choose one of the listed icons." });
        }

        if (!string.IsNullOrWhiteSpace(request.ExpiresAt))
        {
            if (!DateOnly.TryParseExact(request.ExpiresAt, "yyyy-MM-dd", out var lastDay))
            {
                return BadRequest(new { error = "Please enter a valid date." });
            }
            if (lastDay < _clock.Today)
            {
                return BadRequest(new { error = "The \"Show until\" date can't be in the past." });
            }
            announcement.ExpiresAt = _clock.StartOfDayUtc(lastDay.AddDays(1));
        }

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(announcement));
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
        icon = a.Icon,
        isActive = a.IsActive,
        isExpired = a.ExpiresAt <= DateTime.UtcNow,
        createdAt = a.CreatedAt,
        lastDay = a.ExpiresAt is { } expiresAt
            ? DateOnly.FromDateTime(_clock.ToLocal(expiresAt).AddTicks(-1)).ToString("yyyy-MM-dd")
            : null
    };
}
