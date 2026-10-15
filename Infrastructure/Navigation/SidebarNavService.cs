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

    public async Task<List<SidebarNavGroup>> GetNavGroupsForUserAsync(ClaimsPrincipal user)
    {
        var filePath = Path.Combine(_env.WebRootPath, "data", "admin-nav.json");
        if (!File.Exists(filePath)) return new List<SidebarNavGroup>();

        var json = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<SidebarNavConfig>(json, options);

        return config?.Groups
            .Select(group => new SidebarNavGroup
            {
                Title = group.Title,
                NavLinks = (group.NavLinks ?? new List<SidebarNavItem>())
                    //.Where(item => item.Roles.Count == 0 || item.Roles.Any(user.IsInRole))
                    .ToList()
            })
            .Where(group => group.NavLinks.Count > 0)
            .ToList() ?? new List<SidebarNavGroup>();
    }
}