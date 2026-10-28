namespace RestaurantReservation.Infrastructure.Email;

// The "Email" section of appsettings.json. By default it points at Mailpit (https://mailpit.axllent.org),
// a local mail catcher: every email sent is shown at http://localhost:8025 and none reach a real inbox.
public class EmailSettings
{
    // Turn off to skip sending (announcements still show in the customer bell).
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool EnableSsl { get; set; }

    // Leave empty for Mailpit, which needs no login.
    public string? UserName { get; set; }
    public string? Password { get; set; }

    // The customer site's address, for links in emails sent without a web request to take it from
    // (e.g. a promotion emailed on its start date). Matches the https profile in launchSettings.json.
    public string SiteUrl { get; set; } = "https://localhost:7299";

    public string FromAddress { get; set; } = "no-reply@resto.local";
    public string FromName { get; set; } = "Resto";
}
