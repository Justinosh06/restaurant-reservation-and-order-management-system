using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Announcements;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Infrastructure.Email;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Admin.Announcements;

[AdministratorChangesOnly]
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
    private readonly AnnouncementEmailQueue _emailQueue;
    private readonly EmailSettings _emailSettings;

    // Set when this request queued the customer emails, so the page can say so.
    private bool _emailQueued;

    public AnnouncementModel(ApplicationDbContext db, RestaurantClock clock, AnnouncementEmailQueue emailQueue,
        IOptions<EmailSettings> emailSettings)
    {
        _db = db;
        _clock = clock;
        _emailQueue = emailQueue;
        _emailSettings = emailSettings.Value;
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
        await EmailCustomersIfNewlyLiveAsync(announcement);

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
        await EmailCustomersIfNewlyLiveAsync(announcement);

        return new JsonResult(ToDto(announcement));
    }

    // Customers are emailed the first time an announcement is live: when it is posted visible, or when a hidden
    // one is switched on later. PublishedAt is saved before queueing, so editing it afterwards never emails again.
    // The queue is in memory: emails not yet sent when the app stops are not sent later.
    private async Task EmailCustomersIfNewlyLiveAsync(Announcement announcement)
    {
        if (announcement.PublishedAt is not null || !AnnouncementFeed.IsLive(announcement))
        {
            return;
        }

        // Set even with email turned off: the notification bells count the announcement as new from this time.
        announcement.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (!_emailSettings.Enabled)
        {
            return;
        }

        _emailQueue.Enqueue(new AnnouncementEmailJob(announcement.Id, SiteHosts.MainSiteUrl(Request, "/Customer/Notifications")));
        _emailQueued = true;
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
        await EmailCustomersIfNewlyLiveAsync(announcement);

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
        publishedAt = a.PublishedAt,
        emailQueued = _emailQueued,
        lastDay = a.ExpiresAt.HasValue
            ? DateOnly.FromDateTime(_clock.ToLocal(a.ExpiresAt.Value).AddTicks(-1)).ToString("yyyy-MM-dd")
            : null
    };
}
