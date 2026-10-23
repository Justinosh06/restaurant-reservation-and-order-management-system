using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantReservation.Services;

// One-time 6-digit login codes for customers without a password (see Pages/Login).
// One code per email: it lasts 10 minutes and allows 5 tries, and a new code can be asked for after 60 seconds.
// Only a hash of the code is kept, in memory, so codes are gone when the app restarts.
public class LoginCodeService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendWait = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    public enum CheckResult
    {
        Valid,
        Wrong,
        Expired,
        TooManyAttempts
    }

    private sealed class Entry
    {
        public required byte[] CodeHash { get; init; }
        public required DateTime IssuedAt { get; init; }
        public int Attempts { get; set; }
        public DateTime ExpiresAt => IssuedAt + Lifetime;
    }

    private readonly ConcurrentDictionary<string, Entry> _codes = new();

    // A new code for email, or null with how long to wait when one was sent less than a minute ago.
    public string? Issue(string email, out TimeSpan wait)
    {
        var key = Normalize(email);
        var now = DateTime.UtcNow;
        wait = TimeSpan.Zero;

        if (_codes.TryGetValue(key, out var current) && now - current.IssuedAt < ResendWait)
        {
            wait = ResendWait - (now - current.IssuedAt);
            return null;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes[key] = new Entry { CodeHash = Hash(code), IssuedAt = now };
        return code;
    }

    // A valid code can only be used once.
    public CheckResult Check(string email, string? code)
    {
        var key = Normalize(email);
        if (!_codes.TryGetValue(key, out var entry) || DateTime.UtcNow > entry.ExpiresAt)
        {
            _codes.TryRemove(key, out _);
            return CheckResult.Expired;
        }

        if (entry.Attempts >= MaxAttempts)
        {
            return CheckResult.TooManyAttempts;
        }

        var typed = (code ?? string.Empty).Trim();
        if (typed.Length == 6 && CryptographicOperations.FixedTimeEquals(Hash(typed), entry.CodeHash))
        {
            _codes.TryRemove(key, out _);
            return CheckResult.Valid;
        }

        entry.Attempts++;
        return entry.Attempts >= MaxAttempts ? CheckResult.TooManyAttempts : CheckResult.Wrong;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
