using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using RestaurantReservation.Infrastructure.Email;

namespace RestaurantReservation.Services;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string? toName, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default);
}

// Sends email over SMTP using the "Email" settings (Mailpit when running locally).
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public SmtpEmailSender(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendAsync(string toAddress, string? toName, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.FromName),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toAddress, toName));
        // Mail apps show the HTML version; the plain text one is the fallback.
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));

        using var client = new SmtpClient(_settings.Host, _settings.Port) { EnableSsl = _settings.EnableSsl };
        if (!string.IsNullOrEmpty(_settings.UserName))
        {
            client.Credentials = new NetworkCredential(_settings.UserName, _settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
