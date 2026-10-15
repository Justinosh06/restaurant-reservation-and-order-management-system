using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Only users with the Customer role can open this page (see AuthorizeFolder in Program.cs).
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
