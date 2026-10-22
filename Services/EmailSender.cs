using System.Net;
using System.Net.Mail;

namespace RestaurantReservation.Services;

// Sends email over SMTP using the "Email" settings in appsettings.json.
// In development these point at Mailpit (localhost:1025, no login): nothing leaves the computer and every
// message can be read at http://localhost:8025. For a real mail server, set the host, port, SSL and login.
public class EmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // Returns false when the mail server could not be reached, so the page can tell the user to try again.
    public async Task<bool> SendAsync(string toAddress, string subject, string textBody, string htmlBody)
    {
        var settings = _configuration.GetSection("Email");
        var from = new MailAddress(
            settings["FromAddress"] ?? "no-reply@resto.local",
            settings["FromName"] ?? "Resto");

        using var message = new MailMessage(from, new MailAddress(toAddress))
        {
            Subject = subject,
            Body = textBody
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));

        using var client = new SmtpClient(settings["SmtpHost"] ?? "localhost", settings.GetValue("SmtpPort", 1025))
        {
            EnableSsl = settings.GetValue("UseSsl", false)
        };
        if (!string.IsNullOrEmpty(settings["Username"]))
        {
            client.Credentials = new NetworkCredential(settings["Username"], settings["Password"]);
        }

        try
        {
            await client.SendMailAsync(message);
            return true;
        }
        catch (SmtpException error)
        {
            _logger.LogError(error, "Could not send email \"{Subject}\" to {Address}. Is Mailpit running?", subject, toAddress);
            return false;
        }
    }
}
