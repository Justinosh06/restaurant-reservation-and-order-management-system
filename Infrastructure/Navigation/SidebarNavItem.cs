namespace RestaurantReservation.Infrastructure.Navigation;

public class SidebarNavConfig
{
    public List<SidebarNavGroup> Groups { get; set; } = new();
}

public class SidebarNavGroup
{
    public string Title { get; set; } = string.Empty;
    public List<SidebarNavItem> NavLinks { get; set; } = new();
}

public class SidebarNavItem
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<SidebarNavItem>? SubItems { get; set; }
}
