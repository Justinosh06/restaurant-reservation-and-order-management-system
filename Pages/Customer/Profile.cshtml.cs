using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// My profile, the first page of the customer account area. Only customers can open it (see AuthorizeFolder in Program.cs).
// User ID and Email are shown but can never be changed. Gender and date of birth are optional.
public class ProfileModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ProfileModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string UserId { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    // false = view mode (Edit button), true = edit mode (Cancel and Save buttons)
    public bool IsEditing { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

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

        // Gender and date of birth are optional.
        public Gender? Gender { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        public DateOnly? DateOfBirth { get; set; }
    }

    // GET /Customer/Profile            -> view mode
    // GET /Customer/Profile?edit=true  -> edit mode
    public async Task<IActionResult> OnGetAsync(bool edit = false)
    {
        var customer = await _db.Customers.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (customer is null)
        {
            return Challenge();
        }

        UserId = customer.Id;
        Email = customer.Email;
        Input = new InputModel
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            PhoneNumber = customer.PhoneNumber ?? string.Empty,
            Gender = customer.Gender,
            DateOfBirth = customer.DateOfBirth
        };
        IsEditing = edit;
        return Page();
    }

    // Save button
    public async Task<IActionResult> OnPostAsync()
    {
        var customer = await _db.Customers.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (customer is null)
        {
            return Challenge();
        }

        UserId = customer.Id;
        Email = customer.Email;

        if (Input.DateOfBirth > DateOnly.FromDateTime(DateTime.Today))
        {
            ModelState.AddModelError("Input.DateOfBirth", "Date of birth cannot be in the future.");
        }

        if (!ModelState.IsValid)
        {
            IsEditing = true;
            return Page();
        }

        customer.FirstName = Input.FirstName.Trim();
        customer.LastName = Input.LastName.Trim();
        customer.PhoneNumber = Input.PhoneNumber.Trim();
        customer.Gender = Input.Gender;
        customer.DateOfBirth = Input.DateOfBirth;
        await _db.SaveChangesAsync();

        // Refresh the login cookie so the new name shows on other pages straight away.
        var claims = User.Claims
            .Where(c => c.Type != ClaimTypes.Name)
            .Append(new Claim(ClaimTypes.Name, customer.FullName));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        SuccessMessage = "Profile updated.";
        return RedirectToPage();
    }
}
