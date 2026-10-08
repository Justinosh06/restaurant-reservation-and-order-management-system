using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RestaurantReservation.Pages.Table;

public class IndexModel : PageModel
{
    public string TableId { get; set; } = string.Empty;

    public IActionResult OnGet(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return NotFound();
        }

        TableId = tableId.ToUpper();

        return Page();
    }
}