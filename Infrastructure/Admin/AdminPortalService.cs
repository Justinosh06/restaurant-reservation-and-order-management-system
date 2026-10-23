using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Announcements;
using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Admin;

public record AdminPortalInfo(
    string DisplayName,
    string Email,
    string RoleLabel,
    string? ImageUrl,
    string AvatarGender,
    IReadOnlyList<AnnouncementNotice> Notifications,
    int UnreadCount);

// What the admin layout shows for the signed-in account: its name and picture in the sidebar,
// and the announcements in the notification bell. Read from the database on every page, so a
// profile change or a new announcement shows on the next page load.
public class AdminPortalService
{
    private readonly ApplicationDbContext _db;

    public AdminPortalService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AdminPortalInfo> GetAsync(ClaimsPrincipal user)
    {
        var adminId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var admin = adminId is null ? null : await _db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == adminId);

        var email = admin?.Email ?? user.Identity?.Name ?? string.Empty;
        var gender = admin?.Gender?.ToString() ?? user.FindFirstValue("gender");
        var roleLabel = user.IsInRole(nameof(AdminRole.Administrator)) ? "Administrator" : "Staff";

        var seenAt = admin?.AnnouncementsSeenAt;
        var live = await AnnouncementFeed.Live(_db).AsNoTracking().Take(AnnouncementFeed.BellLimit).ToListAsync();
        var unreadCount = await AnnouncementFeed.UnreadSince(_db, seenAt).CountAsync();

        return new AdminPortalInfo(
            DisplayName: string.IsNullOrWhiteSpace(admin?.Name) ? roleLabel : admin.Name,
            Email: email,
            RoleLabel: roleLabel,
            ImageUrl: admin?.ImageUrl,
            AvatarGender: string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase) ? "female" : "male",
            Notifications: live.Select(a => AnnouncementFeed.ToNotice(a, seenAt)).ToList(),
            UnreadCount: unreadCount);
    }
}
