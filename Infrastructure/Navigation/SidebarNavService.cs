using System.Security.Claims;
using System.Text.Json;

namespace Restaurant_Reservation_and_Order_Management_System.Infrastructure.Navigation;

public class SidebarNavService(IWebHostEnvironment environment)
{
    public async Task<List<SidebarNavItem>> GetNavItemsForUserAsync(ClaimsPrincipal user)
    {
        var filePath = Path.Combine(environment.WebRootPath, "data", "admin-nav.json");
        if (!File.Exists(filePath))
        {
            return new List<SidebarNavItem>();
        }

        var json = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<SidebarNavItem>>(json, options) ?? new();

        return items.ToList();
    }
}
