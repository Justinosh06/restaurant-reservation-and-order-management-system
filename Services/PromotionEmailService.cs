using System.Globalization;
using System.Net;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Email;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Services;

// Wakes PromotionEmailWorker straight after a promotion is saved, passing the customer site's address from the
// admin's request for the email link. Without a wake-up the worker still checks every few minutes, which is how
// a promotion scheduled for a later date is emailed on the day it starts.
public class PromotionEmailSignal
{
    private readonly Channel<string> _wakeUps = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Wake(string promotionsPageUrl) => _wakeUps.Writer.TryWrite(promotionsPageUrl);

    public ChannelReader<string> Reader => _wakeUps.Reader;
}

// Emails every customer once about each promotion, when it starts running: straight away for one that starts
// today, on its start date for one scheduled later. EmailedAt is saved before sending, so a promotion is never
// emailed twice, even if it is edited or the app restarts.
public class PromotionEmailWorker : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-MY");

    private readonly PromotionEmailSignal _signal;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly EmailSettings _settings;
    private readonly ILogger<PromotionEmailWorker> _logger;

    // The latest address an admin request gave; until then the configured one.
    private string _promotionsPageUrl;

    public PromotionEmailWorker(PromotionEmailSignal signal, IServiceScopeFactory scopes, IEmailSender sender,
        IOptions<EmailSettings> settings, ILogger<PromotionEmailWorker> logger)
    {
        _signal = signal;
        _scopes = scopes;
        _sender = sender;
        _settings = settings.Value;
        _logger = logger;
        _promotionsPageUrl = _settings.SiteUrl.TrimEnd('/') + "/Customer/Promotions";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendStartedPromotionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not email promotions");
            }

            // Sleep until a promotion is saved or the next regular check, whichever comes first.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(CheckEvery);
            try
            {
                _promotionsPageUrl = await _signal.Reader.ReadAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The regular check is due.
            }
        }
    }

    private async Task SendStartedPromotionsAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<RestaurantClock>().Today;

        var due = await db.Promotions
            .Where(p => p.EmailedAt == null && p.StartDate <= today && p.EndDate >= today)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        foreach (var promotion in due)
        {
            promotion.EmailedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        var restaurantName = await db.RestaurantSettings.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
        restaurantName = string.IsNullOrWhiteSpace(restaurantName) ? "Resto" : restaurantName;
        var currency = await scope.ServiceProvider.GetRequiredService<RestaurantCurrency>().GetAsync();
        var customers = await db.Customers.AsNoTracking()
            .Select(c => new { c.Email, c.FirstName, c.LastName })
            .ToListAsync(cancellationToken);

        foreach (var promotion in due)
        {
            var sent = 0;
            foreach (var customer in customers)
            {
                var name = $"{customer.FirstName} {customer.LastName}".Trim();
                try
                {
                    await _sender.SendAsync(customer.Email, name, $"{restaurantName}: {promotion.Title}",
                        HtmlBody(restaurantName, name, promotion, currency, _promotionsPageUrl),
                        TextBody(restaurantName, name, promotion, currency, _promotionsPageUrl),
                        cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not email promotion {PromotionId} to {Email}", promotion.Id, customer.Email);
                }
            }

            _logger.LogInformation("Emailed promotion {PromotionId} to {Sent} of {Total} customers", promotion.Id, sent, customers.Count);
        }
    }

    // e.g. "20% off" or "RM 10.00 off", plus the minimum spend when there is one.
    private static string? OfferText(Promotion promotion, CurrencyType currency)
    {
        if (promotion.DiscountType is null)
        {
            return null;
        }

        var offer = promotion.DiscountType == PromotionDiscountType.Percentage
            ? $"{promotion.DiscountValue}% off"
            : $"{currency.FormatCents(promotion.DiscountValue)} off";
        return promotion.MinimumSpend > 0 ? $"{offer} when you spend {currency.FormatCents(promotion.MinimumSpend)} or more" : offer;
    }

    private static string DatesText(Promotion promotion) =>
        $"Valid until {promotion.EndDate.ToString("d MMMM yyyy", Culture)}";

    private static string Greeting(string name) => string.IsNullOrEmpty(name) ? "Hi there," : $"Hi {name},";

    private static string TextBody(string restaurant, string name, Promotion promotion, CurrencyType currency, string pageUrl)
    {
        var offer = OfferText(promotion, currency);
        return $"{Greeting(name)}\n\n{promotion.Title}\n\n{promotion.Description}\n\n"
            + (offer is null ? "" : $"{offer}\n")
            + (promotion.Code is null ? "" : $"Use code {promotion.Code} at checkout.\n")
            + $"{DatesText(promotion)}\n\nSee our promotions: {pageUrl}\n\n{restaurant}";
    }

    private static string HtmlBody(string restaurant, string name, Promotion promotion, CurrencyType currency, string pageUrl)
    {
        string E(string value) => WebUtility.HtmlEncode(value);
        var description = E(promotion.Description).Replace("\r\n", "\n").Replace("\n", "<br>");
        var offer = OfferText(promotion, currency);
        // Uploaded images are stored as site paths ("/uploads/promotions/..."), so they need the site's address.
        var siteRoot = new Uri(pageUrl).GetLeftPart(UriPartial.Authority);
        var image = promotion.ImageUrl is null
            ? ""
            : $"""<tr><td style="padding:20px 32px 0;"><img src="{E(siteRoot + "/" + promotion.ImageUrl.TrimStart('~', '/'))}" alt="" width="496" style="display:block;width:100%;max-width:496px;border-radius:12px;"></td></tr>""";
        var offerRow = offer is null
            ? ""
            : $"""<tr><td style="padding:16px 32px 0;font-size:18px;font-weight:700;">{E(offer)}</td></tr>""";
        var codeRow = promotion.Code is null
            ? ""
            : $"""
                <tr><td style="padding:12px 32px 0;font-size:14px;color:#555;">
                  Use code <span style="display:inline-block;padding:4px 12px;border-radius:8px;background:#f2f2f3;color:#111;font-family:monospace;font-size:16px;font-weight:700;letter-spacing:1px;">{E(promotion.Code)}</span> at checkout
                </td></tr>
                """;

        return $"""
            <!doctype html>
            <html>
            <body style="margin:0;padding:24px;background:#f2f2f3;font-family:Arial,Helvetica,sans-serif;color:#111;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#fff;border-radius:16px;">
                <tr><td style="padding:28px 32px 0;font-size:24px;font-weight:700;letter-spacing:-1px;">{E(restaurant)}</td></tr>
                <tr><td style="padding:20px 32px 0;font-size:15px;">{E(Greeting(name))}</td></tr>
                {image}
                <tr><td style="padding:16px 32px 0;">
                  <span style="display:inline-block;padding:4px 12px;border-radius:100px;background:#f2f2f3;color:#555;font-size:12px;font-weight:700;">New promotion</span>
                  <h1 style="margin:12px 0 0;font-size:22px;">{E(promotion.Title)}</h1>
                </td></tr>
                <tr><td style="padding:12px 32px 0;font-size:15px;line-height:1.6;color:#333;">{description}</td></tr>
                {offerRow}
                {codeRow}
                <tr><td style="padding:12px 32px 0;font-size:13px;color:#888;">{E(DatesText(promotion))}</td></tr>
                <tr><td style="padding:24px 32px 32px;">
                  <a href="{E(pageUrl)}" style="display:inline-block;padding:12px 24px;border-radius:100px;background:#000;color:#fff;font-weight:700;text-decoration:none;">See our promotions</a>
                </td></tr>
              </table>
              <p style="max-width:560px;margin:16px auto 0;font-size:12px;color:#888;text-align:center;">You are receiving this because you have an account at {E(restaurant)}.</p>
            </body>
            </html>
            """;
    }
}
