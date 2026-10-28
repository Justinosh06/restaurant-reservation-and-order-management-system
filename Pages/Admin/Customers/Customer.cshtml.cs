using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Customers;

// Admins can look up customer accounts and delete them, but not edit their details (customers manage those
// themselves). Every query selects only the fields shown, so the password hash is never read from the database.
public class CustomerModel : PageModel
{
    private const int HistoryLimit = 10;

    public record DeleteCustomerRequest(string Id);

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;

    public CustomerModel(ApplicationDbContext db, RestaurantClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        var customers = await _db.Customers.AsNoTracking()
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName).ThenBy(c => c.Email)
            .Select(c => new
            {
                c.Id,
                c.Email,
                c.FirstName,
                c.LastName,
                c.PhoneNumber,
                Reservations = _db.Reservations.Count(r => r.CustomerId == c.Id),
                Orders = _db.Orders.Count(o => o.CustomerId == c.Id && o.Status != OrderStatus.Cancelled),
                Spent = _db.Orders.Where(o => o.CustomerId == c.Id && RevenueRules.Statuses.Contains(o.Status)).Sum(o => (int?)o.TotalAmount) ?? 0,
                LastVisit = _db.Sessions.Where(s => s.CustomerId == c.Id).Max(s => (DateTime?)s.StartingTime)
            })
            .ToListAsync();

        return new JsonResult(customers.Select(c => new
        {
            id = c.Id,
            displayId = AdminDisplay.ShortId(c.Id),
            name = $"{c.FirstName} {c.LastName}".Trim(),
            email = c.Email,
            phone = c.PhoneNumber,
            reservations = c.Reservations,
            orders = c.Orders,
            spent = c.Spent,
            lastVisit = c.LastVisit
        }));
    }

    public async Task<IActionResult> OnGetDetailAsync(string id)
    {
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Email, c.FirstName, c.LastName, c.PhoneNumber, c.Gender, c.DateOfBirth })
            .FirstOrDefaultAsync();
        if (customer is null)
        {
            return NotFound(new { error = "This customer account no longer exists. Refresh the page." });
        }

        var reservations = await _db.Reservations.AsNoTracking()
            .Where(r => r.CustomerId == id)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.TimeSlot)
            .Take(HistoryLimit)
            .Select(r => new { r.Id, r.Date, r.TimeSlot, r.Pax, r.Status })
            .ToListAsync();

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.CreatedAt)
            .Take(HistoryLimit)
            .Select(o => new { o.Id, o.CreatedAt, o.Status, o.TotalAmount, o.Session!.TableId, Items = o.OrderItems.Sum(i => (int?)i.Quantity) ?? 0 })
            .ToListAsync();

        var now = _clock.Now;
        return new JsonResult(new
        {
            id = customer.Id,
            displayId = AdminDisplay.ShortId(customer.Id),
            name = $"{customer.FirstName} {customer.LastName}".Trim(),
            email = customer.Email,
            phone = customer.PhoneNumber,
            gender = customer.Gender?.ToString(),
            dateOfBirth = customer.DateOfBirth,
            reservations = reservations.Select(r => new
            {
                displayId = AdminDisplay.ShortId(r.Id),
                date = r.Date,
                time = r.TimeSlot.ToString("HH:mm"),
                pax = r.Pax,
                status = ReservationAnalytics.Outcome(r.Status, r.Date, r.TimeSlot, now) == ReservationOutcome.NoShow ? "No-show"
                    : r.Status == ReservationStatus.CheckedIn ? "Checked in"
                    : r.Status.ToString()
            }),
            orders = orders.Select(o => new
            {
                displayId = AdminDisplay.ShortId(o.Id),
                createdAt = o.CreatedAt,
                status = AdminDisplay.OrderStatusLabel(o.Status),
                table = o.TableId,
                items = o.Items,
                total = o.TotalAmount
            })
        });
    }

    // Deletes the account but keeps the restaurant's records: past orders, bookings and dining sessions stay
    // (shown as a guest), so revenue and analytics don't change. Their reviews are removed, since a review
    // must belong to a customer. Not allowed while they are dining or have a booking still to come: cancel
    // the booking first, so a paid deposit is refunded the usual way.
    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteCustomerRequest request)
    {
        var customer = await _db.Customers.FindAsync(request.Id);
        if (customer is null)
        {
            return NotFound(new { error = "This customer account no longer exists. Refresh the page." });
        }

        if (await _db.Sessions.AnyAsync(s => s.CustomerId == customer.Id && s.EndingTime == null))
        {
            return BadRequest(new { error = "This customer is dining right now. Delete the account after their session ends." });
        }

        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var upcoming = (await _db.Reservations.AsNoTracking()
                .Where(r => r.CustomerId == customer.Id && r.Date >= today
                    && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn))
                .Select(r => new { r.Date, r.TimeSlot })
                .ToListAsync())
            .Count(r => r.Date.ToDateTime(r.TimeSlot) > now);
        if (upcoming > 0)
        {
            return BadRequest(new
            {
                error = $"This customer has {upcoming} upcoming {(upcoming == 1 ? "booking" : "bookings")}. "
                    + "Cancel them in Reservation Management first, so any deposit is refunded."
            });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _db.Orders.Where(o => o.CustomerId == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerId, (string?)null));
        // IgnoreQueryFilters: bookings still waiting for their deposit are hidden by default but still point here.
        await _db.Reservations.IgnoreQueryFilters().Where(r => r.CustomerId == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.CustomerId, (string?)null));
        await _db.Sessions.Where(x => x.CustomerId == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (string?)null));
        await _db.Feedbacks.Where(f => f.CustomerId == customer.Id).ExecuteDeleteAsync();
        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new NoContentResult();
    }
}
