using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

public class PrivacyModel : PageModel
{
    // The privacy policy lives with the other policies now (Pages/Policies).
    public IActionResult OnGet() => RedirectPermanent("/Policies/privacy");
}

