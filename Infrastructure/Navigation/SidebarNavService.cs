namespace RestaurantReservation.Infrastructure.Navigation;

using System.Text.Json;
using System.Security.Claims;

public class SidebarNavService
{
    private readonly IWebHostEnvironment _env;

    public SidebarNavService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<List<SidebarNavItem>> GetNavItemsForUserAsync(ClaimsPrincipal user)
    {
        var filePath = Path.Combine(_env.WebRootPath, "data", "admin-nav.json");
        if (!File.Exists(filePath)) return new List<SidebarNavItem>();

        var json = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var allItems = JsonSerializer.Deserialize<List<SidebarNavItem>>(json, options) ?? new();

        // Filter items matching the logged-in user's roles
        /*return allItems.Where(item => 
            !item.Roles.Any() || item.Roles.Any(role => user.IsInRole(role))
        ).ToList();*/

        return allItems.ToList();
    }
}