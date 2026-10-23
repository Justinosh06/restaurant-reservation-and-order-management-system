using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;

namespace RestaurantReservation.Infrastructure.Announcements;

public record CustomerNotifications(IReadOnlyList<AnnouncementNotice> Items, int UnreadCount);

// The announcement bell in the customer site's navigation bar (Pages/Shared/_LandingNav.cshtml).
public class CustomerNotificationService
{
    private readonly ApplicationDbContext _db;

    public CustomerNotificationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CustomerNotifications> GetAsync(ClaimsPrincipal user)
    {
        var customerId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var seenAt = customerId is null
            ? null
            : await _db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.AnnouncementsSeenAt).FirstOrDefaultAsync();

        var live = await AnnouncementFeed.Live(_db).AsNoTracking().Take(AnnouncementFeed.BellLimit).ToListAsync();
        var unreadCount = await AnnouncementFeed.UnreadSince(_db, seenAt).CountAsync();

        return new CustomerNotifications(live.Select(a => AnnouncementFeed.ToNotice(a, seenAt)).ToList(), unreadCount);
    }
}
