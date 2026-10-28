using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Announcements;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Every announcement from the restaurant that is live right now. The emailed announcements link here.
public class NotificationsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NotificationsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<AnnouncementNotice> Announcements { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var customer = await CurrentCustomerAsync();
        var seenAt = customer?.AnnouncementsSeenAt;

        var announcements = await AnnouncementFeed.Live(_db).AsNoTracking().ToListAsync();
        Announcements = announcements.Select(a => AnnouncementFeed.ToNotice(a, seenAt)).ToList();

        // Opening this page counts as reading them. The "New" marks above were worked out first, so they still show this time.
        await MarkSeenAsync(customer);
    }

    // Called by js/customer-notifications.js when the bell in the navigation bar is opened.
    public async Task<IActionResult> OnPostSeenAsync()
    {
        await MarkSeenAsync(await CurrentCustomerAsync());
        return new NoContentResult();
    }

    private async Task<RestaurantReservation.Models.Customer?> CurrentCustomerAsync()
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return customerId is null ? null : await _db.Customers.FindAsync(customerId);
    }

    private async Task MarkSeenAsync(RestaurantReservation.Models.Customer? customer)
    {
        if (customer is null)
        {
            return;
        }

        customer.AnnouncementsSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
