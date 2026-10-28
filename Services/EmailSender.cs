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

// Sends one-off emails (such as login codes) through IEmailSender, so it uses the same "Email" settings.
// In development these point at Mailpit (localhost:1025, no login): nothing leaves the computer and every
// message can be read at http://localhost:8025. For a real mail server, set the host, port, SSL and login.
public class EmailSender
{
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IEmailSender sender, ILogger<EmailSender> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    // Returns false when the mail server could not be reached, so the page can tell the user to try again.
    public async Task<bool> SendAsync(string toAddress, string subject, string textBody, string htmlBody)
    {
        try
        {
            await _sender.SendAsync(toAddress, null, subject, htmlBody, textBody);
            return true;
        }
        catch (SmtpException error)
        {
            _logger.LogError(error, "Could not send email \"{Subject}\" to {Address}. Is Mailpit running?", subject, toAddress);
            return false;
        }
    }
}
