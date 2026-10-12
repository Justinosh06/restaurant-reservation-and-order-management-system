using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace RestaurantReservation.Services;

// Hands an Admin/Staff login over from the main site to the admin subdomain.
// The main-site login cookie is not sent to admin.localhost, so after a successful login
// the user is redirected there with a random one-time ticket that expires after one minute.
public class AdminSignInTicketService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, (string AdminId, DateTime ExpiresAt)> _tickets = new();

    public string Create(string adminId)
    {
        RemoveExpired();

        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (adminId, DateTime.UtcNow.Add(Lifetime));
        return ticket;
    }

    // Returns the admin ID once; the ticket cannot be used again.
    public string? Redeem(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || !_tickets.TryRemove(ticket, out var entry))
        {
            return null;
        }

        return entry.ExpiresAt > DateTime.UtcNow ? entry.AdminId : null;
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _tickets)
        {
            if (entry.ExpiresAt <= now)
            {
                _tickets.TryRemove(key, out _);
            }
        }
    }
}
