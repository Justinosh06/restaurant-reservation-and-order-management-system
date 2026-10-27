using System.Globalization;
using System.Net;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Email;
using RestaurantReservation.Models;

namespace RestaurantReservation.Services;

public enum ReservationEmailKind
{
    Confirmed,
    Modified,
    Cancelled
}

// The booking details an email shows, copied when the change is made: a deleted booking is gone by the
// time the email is sent, and a modified one needs its old details to say what changed.
public record ReservationEmailDetails(DateOnly Date, TimeOnly Time, int Guests)
{
    public static ReservationEmailDetails From(Reservation reservation) =>
        new(reservation.Date, reservation.TimeSlot, reservation.Pax);
}

public record ReservationEmailJob(
    string CustomerId,
    ReservationEmailKind Kind,
    ReservationEmailDetails Booking,
    ReservationEmailDetails? Before = null,
    bool DepositRefunded = false);

// Reservation emails waiting to be sent. Pages add to it and carry on; ReservationEmailWorker sends them in
// the background, so a slow or unreachable mail server never holds up or breaks a booking change.
// Bookings staff take by phone have no customer account, so they get no email. Nor does a booking whose time
// has passed: staff tidying up yesterday's records (seating, correcting or cancelling a past booking) must not
// tell the customer about a visit that is over.
public class ReservationEmailQueue
{
    private readonly Channel<ReservationEmailJob> _jobs = Channel.CreateUnbounded<ReservationEmailJob>();
    private readonly RestaurantClock _clock;

    public ReservationEmailQueue(RestaurantClock clock)
    {
        _clock = clock;
    }

    public void Confirmed(Reservation reservation) =>
        Enqueue(reservation, ReservationEmailKind.Confirmed);

    public void Modified(Reservation reservation, ReservationEmailDetails before) =>
        Enqueue(reservation, ReservationEmailKind.Modified, before);

    public void Cancelled(Reservation reservation, bool depositRefunded = false) =>
        Enqueue(reservation, ReservationEmailKind.Cancelled, depositRefunded: depositRefunded);

    public IAsyncEnumerable<ReservationEmailJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);

    private void Enqueue(Reservation reservation, ReservationEmailKind kind, ReservationEmailDetails? before = null, bool depositRefunded = false)
    {
        if (reservation.CustomerId is not null && reservation.Date.ToDateTime(reservation.TimeSlot) > _clock.Now)
        {
            _jobs.Writer.TryWrite(new ReservationEmailJob(
                reservation.CustomerId, kind, ReservationEmailDetails.From(reservation), before, depositRefunded));
        }
    }
}

public class ReservationEmailWorker : BackgroundService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-MY");

    private readonly ReservationEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly EmailSettings _settings;
    private readonly ILogger<ReservationEmailWorker> _logger;

    public ReservationEmailWorker(ReservationEmailQueue queue, IServiceScopeFactory scopes, IEmailSender sender,
        IOptions<EmailSettings> settings, ILogger<ReservationEmailWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _sender = sender;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not send the {Kind} reservation email to customer {CustomerId}", job.Kind, job.CustomerId);
            }
        }
    }

    private async Task SendAsync(ReservationEmailJob job, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == job.CustomerId)
            .Select(c => new { c.Email, c.FirstName, c.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return;
        }

        var restaurant = await db.RestaurantSettings.AsNoTracking()
            .Select(s => new { s.Name, s.Address, s.PhoneNumber })
            .FirstOrDefaultAsync(cancellationToken);
        var restaurantName = string.IsNullOrWhiteSpace(restaurant?.Name) ? "Resto" : restaurant.Name;
        var name = $"{customer.FirstName} {customer.LastName}".Trim();

        var (subject, heading, message) = job.Kind switch
        {
            ReservationEmailKind.Confirmed => (
                "Your booking is confirmed",
                "Your booking is confirmed",
                "Your table is ready for you. We look forward to seeing you."),
            ReservationEmailKind.Modified => (
                "Your booking has been changed",
                "Your booking has been changed",
                job.Before is null ? "Your booking details have been updated." : $"It was {Describe(job.Before)}. Here are the new details."),
            _ => (
                "Your booking has been cancelled",
                "Your booking has been cancelled",
                job.DepositRefunded
                    ? "Your deposit has been refunded to your card. It usually arrives within 5-10 working days."
                    : "We hope to see you another time.")
        };

        var contact = restaurant?.PhoneNumber is { Length: > 0 } phone
            ? $"Questions or changes? Call us at {phone}."
            : "Questions or changes? Please contact the restaurant.";

        await _sender.SendAsync(customer.Email, name, $"{restaurantName}: {subject}",
            HtmlBody(restaurantName, restaurant?.Address, name, heading, message, job.Booking, contact),
            TextBody(restaurantName, restaurant?.Address, name, heading, message, job.Booking, contact),
            cancellationToken);

        _logger.LogInformation("Sent the {Kind} reservation email to customer {CustomerId}", job.Kind, job.CustomerId);
    }

    private static string DateText(ReservationEmailDetails booking) => booking.Date.ToString("dddd, d MMMM yyyy", Culture);

    private static string TimeText(ReservationEmailDetails booking) => booking.Time.ToString("h:mm tt", Culture);

    private static string GuestsText(ReservationEmailDetails booking) => booking.Guests == 1 ? "1 guest" : $"{booking.Guests} guests";

    private static string Describe(ReservationEmailDetails booking) =>
        $"{booking.Date.ToString("ddd d MMM", Culture)} at {TimeText(booking)} for {GuestsText(booking)}";

    private static string Greeting(string name) => string.IsNullOrEmpty(name) ? "Hi there," : $"Hi {name},";

    private static string TextBody(string restaurant, string? address, string name, string heading, string message,
        ReservationEmailDetails booking, string contact) =>
        $"{Greeting(name)}\n\n{heading}\n\n{message}\n\n"
        + $"Date: {DateText(booking)}\nTime: {TimeText(booking)}\nGuests: {GuestsText(booking)}\n"
        + (string.IsNullOrWhiteSpace(address) ? "" : $"Where: {address}\n")
        + $"\n{contact}\n\n{restaurant}";

    private static string HtmlBody(string restaurant, string? address, string name, string heading, string message,
        ReservationEmailDetails booking, string contact)
    {
        string E(string value) => WebUtility.HtmlEncode(value);
        string Row(string label, string value) => $"""
            <tr>
              <td style="padding:8px 0;color:#888;font-size:14px;width:90px;">{E(label)}</td>
              <td style="padding:8px 0;font-size:15px;font-weight:700;">{E(value)}</td>
            </tr>
            """;

        var where = string.IsNullOrWhiteSpace(address) ? "" : Row("Where", address);

        return $"""
            <!doctype html>
            <html>
            <body style="margin:0;padding:24px;background:#f2f2f3;font-family:Arial,Helvetica,sans-serif;color:#111;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#fff;border-radius:16px;">
                <tr><td style="padding:28px 32px 0;font-size:24px;font-weight:700;letter-spacing:-1px;">{E(restaurant)}</td></tr>
                <tr><td style="padding:20px 32px 0;font-size:15px;">{E(Greeting(name))}</td></tr>
                <tr><td style="padding:12px 32px 0;"><h1 style="margin:0;font-size:22px;">{E(heading)}</h1></td></tr>
                <tr><td style="padding:12px 32px 0;font-size:15px;line-height:1.6;color:#333;">{E(message)}</td></tr>
                <tr><td style="padding:16px 32px 0;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f7f8;border-radius:12px;padding:8px 16px;">
                    {Row("Date", DateText(booking))}
                    {Row("Time", TimeText(booking))}
                    {Row("Guests", GuestsText(booking))}
                    {where}
                  </table>
                </td></tr>
                <tr><td style="padding:20px 32px 32px;font-size:14px;color:#555;">{E(contact)}</td></tr>
              </table>
              <p style="max-width:560px;margin:16px auto 0;font-size:12px;color:#888;text-align:center;">You are receiving this because you booked a table at {E(restaurant)}.</p>
            </body>
            </html>
            """;
    }
}
