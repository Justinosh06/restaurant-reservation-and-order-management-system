using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace RestaurantReservation.Infrastructure.Announcements;

// PostedAt is when the announcement first became visible to customers (see Announcement.PublishedAt).
public record AnnouncementNotice(string Id, string Title, string Description, string Category, DateTime PostedAt, bool IsUnread);

// The announcements shown in the notification bells of the admin portal and the customer site.
public static class AnnouncementFeed
{
    // How many announcements a bell lists; the rest are on the Notifications page.
    public const int BellLimit = 6;

    // Announcements customers can see right now (switched on and not past their "Show until" day), newest first.
    public static IQueryable<Announcement> Live(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        return db.Announcements
            .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > now))
            .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt);
    }

    public static bool IsLive(Announcement a) =>
        a.IsActive && (a.ExpiresAt is null || a.ExpiresAt > DateTime.UtcNow);

    // How many live announcements were posted after the reader last opened their bell.
    public static IQueryable<Announcement> UnreadSince(ApplicationDbContext db, DateTime? seenAt) =>
        Live(db).Where(a => seenAt == null || (a.PublishedAt ?? a.CreatedAt) > seenAt);

    public static AnnouncementNotice ToNotice(Announcement a, DateTime? seenAt)
    {
        var postedAt = a.PublishedAt ?? a.CreatedAt;
        return new(a.Id, a.Title, a.Description, a.Category, postedAt, seenAt is null || postedAt > seenAt);
    }
}
