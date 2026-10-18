using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RestaurantReservation.Pages.Table.MenuItem;

public class MenuItemModel : PageModel
{
    public string TableId { get; set; } = string.Empty;
    public string MenuItemId { get; set; } = string.Empty;

    // Parameter name MUST match {menuItemId} from the @page directive
    public IActionResult OnGet(string tableId, string menuItemId)
    {
        if (string.IsNullOrWhiteSpace(menuItemId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpper();
        MenuItemId = menuItemId.ToUpper();

        // Example: Validate or fetch item from DB
        // var item = _db.MenuItems.FirstOrDefault(m => m.Id == MenuItemId);
        // if (item == null) return NotFound();

        return Page();
    }
}