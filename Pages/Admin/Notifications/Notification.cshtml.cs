using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Announcements;

namespace RestaurantReservation.Pages.Admin.Notifications;

// Every announcement customers can see right now, for Administrators and Staff alike
// (the Announcements page that edits them is for Administrators only).
public class NotificationModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NotificationModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<AnnouncementNotice> Announcements { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var admin = await CurrentAdminAsync();
        var seenAt = admin?.AnnouncementsSeenAt;

        var announcements = await AnnouncementFeed.Live(_db).AsNoTracking().ToListAsync();
        Announcements = announcements.Select(a => AnnouncementFeed.ToNotice(a, seenAt)).ToList();

        // Opening this page counts as reading them. The "new" marks above were worked out first, so they still show this time.
        await MarkSeenAsync(admin);
    }

    // Called by js/admin/notifications.js when the sidebar bell is opened.
    public async Task<IActionResult> OnPostSeenAsync()
    {
        await MarkSeenAsync(await CurrentAdminAsync());
        return new NoContentResult();
    }

    private async Task<Models.Admin?> CurrentAdminAsync()
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return adminId is null ? null : await _db.Admins.FindAsync(adminId);
    }

    private async Task MarkSeenAsync(Models.Admin? admin)
    {
        if (admin is null)
        {
            return;
        }

        admin.AnnouncementsSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
