using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// "Complete your profile": customers whose account has only an email (made by a login code or a guest checkout)
// add their name and phone number here before using the site (see Infrastructure/Authentication/ProfileSetup).
public class SetupModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SetupModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    // Same rules as My profile.
    public class InputModel
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(50)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\+?[0-9\- ]{8,20}$", ErrorMessage = "Please enter a valid phone number.")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        var customer = await _db.Customers.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (customer is null)
        {
            return Challenge();
        }

        // Nothing to set up: carry on.
        if (ProfileSetup.IsComplete(customer))
        {
            return GoOn(returnUrl);
        }

        Email = customer.Email;
        Input = new InputModel
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            PhoneNumber = customer.PhoneNumber ?? string.Empty
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var customer = await _db.Customers.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (customer is null)
        {
            return Challenge();
        }

        Email = customer.Email;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        customer.FirstName = Input.FirstName.Trim();
        customer.LastName = Input.LastName.Trim();
        customer.PhoneNumber = Input.PhoneNumber.Trim();
        await _db.SaveChangesAsync();

        // New login cookie: the real name for "Hi, ..." and the profile marked complete.
        await ProfileSetup.SignInCustomerAsync(HttpContext, customer);
        return GoOn(returnUrl);
    }

    // Only local URLs, and never back to this page.
    private IActionResult GoOn(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) && !returnUrl!.StartsWith(ProfileSetup.SetupPath, StringComparison.OrdinalIgnoreCase)
            ? LocalRedirect(returnUrl)
            : RedirectToPage("/Index");
}
