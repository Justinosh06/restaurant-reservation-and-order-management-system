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

        // Optional: Validate table existence in DB or cache
        // var tableExists = _db.Tables.Any(t => t.Code == TableId);
        // if (!tableExists) return NotFound("Invalid table QR code or ID.");

        return Page();
    }
}