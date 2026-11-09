using System.Net;
using System.Net.Sockets;

namespace RestaurantReservation.Infrastructure.Authentication;

// Builds links between the main site (localhost) and the admin portal (admin.localhost).
// Login cookies are per host, so each site keeps its own login.
public static class SiteHosts
{
    public const string AdminSubdomain = "admin";

    private const string AdminPrefix = AdminSubdomain + ".";

    public static bool IsAdminHost(HttpRequest request) =>
        request.Host.Host.StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase);

    public static string MainSiteUrl(HttpRequest request, string path)
    {
        var host = request.Host.Host;
        if (host.StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase))
        {
            host = host[AdminPrefix.Length..];
        }

        return BuildUrl(request, host, path);
    }

    public static string AdminSiteUrl(HttpRequest request, string path) =>
        IsAdminHost(request)
            ? BuildUrl(request, request.Host.Host, path)
            : BuildUrl(request, AdminPrefix + AddressableHost(request.Host.Host), path);

    // An IP address can't have a subdomain, so on a LAN the admin portal uses nip.io,
    // a public DNS service where admin.192.168.1.20.nip.io resolves to 192.168.1.20.
    private static string AddressableHost(string host) =>
        IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetwork
            ? $"{host}.nip.io"
            : host;

    private static string BuildUrl(HttpRequest request, string host, string path)
    {
        var port = request.Host.Port is { } p ? $":{p}" : string.Empty;
        return $"{request.Scheme}://{host}{port}{path}";
    }
}
