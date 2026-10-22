using System.Net;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Announcements;
using RestaurantReservation.Infrastructure.Email;

namespace RestaurantReservation.Services;

// An announcement to email, and the customer site's address for the "View on our website" link
// (taken from the admin's request, since the background sender has no request of its own).
public record AnnouncementEmailJob(string AnnouncementId, string SiteUrl);

// Announcements waiting to be emailed. The Announcements page adds to it and returns straight away;
// AnnouncementEmailWorker sends the emails in the background, so posting doesn't wait for every customer.
public class AnnouncementEmailQueue
{
    private readonly Channel<AnnouncementEmailJob> _jobs = Channel.CreateUnbounded<AnnouncementEmailJob>();

    public void Enqueue(AnnouncementEmailJob job) => _jobs.Writer.TryWrite(job);

    public IAsyncEnumerable<AnnouncementEmailJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);
}

public class AnnouncementEmailWorker : BackgroundService
{
    private readonly AnnouncementEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly EmailSettings _settings;
    private readonly ILogger<AnnouncementEmailWorker> _logger;

    public AnnouncementEmailWorker(AnnouncementEmailQueue queue, IServiceScopeFactory scopes, IEmailSender sender,
        IOptions<EmailSettings> settings, ILogger<AnnouncementEmailWorker> logger)
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
                _logger.LogError(ex, "Could not email announcement {AnnouncementId}", job.AnnouncementId);
            }
        }
    }

    private async Task SendAsync(AnnouncementEmailJob job, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // It may have been hidden or deleted while it waited.
        var announcement = await db.Announcements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == job.AnnouncementId, cancellationToken);
        if (announcement is null || !AnnouncementFeed.IsLive(announcement))
        {
            return;
        }

        var restaurantName = await db.RestaurantSettings.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "Resto";
        var customers = await db.Customers.AsNoTracking()
            .Select(c => new { c.Email, c.FirstName, c.LastName })
            .ToListAsync(cancellationToken);

        var subject = $"{restaurantName}: {announcement.Title}";
        var sent = 0;
        foreach (var customer in customers)
        {
            var name = $"{customer.FirstName} {customer.LastName}".Trim();
            try
            {
                await _sender.SendAsync(customer.Email, name, subject,
                    HtmlBody(restaurantName, name, announcement.Title, announcement.Description, announcement.Category, job.SiteUrl),
                    TextBody(restaurantName, name, announcement.Title, announcement.Description, job.SiteUrl),
                    cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not email announcement {AnnouncementId} to {Email}", announcement.Id, customer.Email);
            }
        }

        _logger.LogInformation("Emailed announcement {AnnouncementId} to {Sent} of {Total} customers",
            announcement.Id, sent, customers.Count);
    }

    private static string Greeting(string name) => string.IsNullOrEmpty(name) ? "Hi there," : $"Hi {name},";

    private static string TextBody(string restaurant, string name, string title, string description, string siteUrl) =>
        $"{Greeting(name)}\n\n{title}\n\n{description}\n\nView it on our website: {siteUrl}\n\n{restaurant}";

    private static string HtmlBody(string restaurant, string name, string title, string description, string category, string siteUrl)
    {
        string E(string value) => WebUtility.HtmlEncode(value);
        var body = E(description).Replace("\r\n", "\n").Replace("\n", "<br>");

        return $"""
            <!doctype html>
            <html>
            <body style="margin:0;padding:24px;background:#f2f2f3;font-family:Arial,Helvetica,sans-serif;color:#111;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#fff;border-radius:16px;">
                <tr><td style="padding:28px 32px 0;font-size:24px;font-weight:700;letter-spacing:-1px;">{E(restaurant)}</td></tr>
                <tr><td style="padding:20px 32px 0;font-size:15px;">{E(Greeting(name))}</td></tr>
                <tr><td style="padding:16px 32px 0;">
                  <span style="display:inline-block;padding:4px 12px;border-radius:100px;background:#f2f2f3;color:#555;font-size:12px;font-weight:700;">{E(category)}</span>
                  <h1 style="margin:12px 0 0;font-size:22px;">{E(title)}</h1>
                </td></tr>
                <tr><td style="padding:12px 32px 0;font-size:15px;line-height:1.6;color:#333;">{body}</td></tr>
                <tr><td style="padding:24px 32px 32px;">
                  <a href="{E(siteUrl)}" style="display:inline-block;padding:12px 24px;border-radius:100px;background:#000;color:#fff;font-weight:700;text-decoration:none;">View on our website</a>
                </td></tr>
              </table>
              <p style="max-width:560px;margin:16px auto 0;font-size:12px;color:#888;text-align:center;">You are receiving this because you have an account at {E(restaurant)}.</p>
            </body>
            </html>
            """;
    }
}
