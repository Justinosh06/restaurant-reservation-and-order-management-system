namespace Restaurant_Reservation_and_Order_Management_System.Infrastructure.Navigation;

public class SidebarNavItem
{
    public string Title { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public string Page { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = new();

    public List<SidebarNavItem>? SubItems { get; set; }
}
