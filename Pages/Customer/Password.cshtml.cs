using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Data;
using RestaurantReservation.Services;
// Alias: "Customer" would otherwise resolve to the Pages.Customer folder.
using CustomerAccount = RestaurantReservation.Models.Customer;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Change password. The customer types the current password and the new one twice,
// then passes the same CAPTCHA step as the login page before anything is checked.
public class PasswordModel : PageModel
{
    public const string AdminEmail = "admin@restaurant.com";

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<CustomerAccount> _passwordHasher;
    private readonly CaptchaService _captcha;

    public PasswordModel(ApplicationDbContext db, IPasswordHasher<CustomerAccount> passwordHasher, CaptchaService captcha)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _captcha = captcha;
    }

    // false = the "Change password" button, true = the form
    public bool IsChanging { get; private set; }

    // Set when the current password was wrong, to point the customer to the admin for a reset.
    public bool WrongCurrentPassword { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        // Same rules as the Register page.
        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
            ErrorMessage = "Password must contain an uppercase letter, a lowercase letter and a number.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Characters")]
        public string? CaptchaAnswer { get; set; }
    }

    // GET /Customer/Password              -> "Change password" button
    // GET /Customer/Password?change=true  -> the form
    public void OnGet(bool change = false)
    {
        IsChanging = change;
    }

    // GET /Customer/Password?handler=Captcha - returns a new CAPTCHA image (called by the popup).
    public IActionResult OnGetCaptcha()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Content(_captcha.CreateCaptchaSvg(HttpContext.Session), "image/svg+xml");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsChanging = true;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Step 1: CAPTCHA, checked before the password like on the login page.
        if (!_captcha.Validate(HttpContext.Session, Input.CaptchaAnswer))
        {
            ModelState.AddModelError(string.Empty, "CAPTCHA verification failed. Please try again.");
            return Page();
        }

        var customer = await _db.Customers.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (customer is null)
        {
            return Challenge();
        }

        // Step 2: the current password must be right.
        if (!IsCurrentPassword(customer, Input.CurrentPassword))
        {
            WrongCurrentPassword = true;
            return Page();
        }

        if (Input.NewPassword == Input.CurrentPassword)
        {
            ModelState.AddModelError("Input.NewPassword", "The new password must be different from the current one.");
            return Page();
        }

        // Step 3: save the new password as a hash.
        customer.Password = _passwordHasher.HashPassword(customer, Input.NewPassword);
        await _db.SaveChangesAsync();

        SuccessMessage = "Your password has been changed.";
        return RedirectToPage();
    }

    private bool IsCurrentPassword(CustomerAccount customer, string password)
    {
        if (string.IsNullOrEmpty(customer.Password))
        {
            return false;
        }

        try
        {
            return _passwordHasher.VerifyHashedPassword(customer, customer.Password, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // The stored value is not a password hash (e.g. typed into the table by hand).
            return false;
        }
    }
}
