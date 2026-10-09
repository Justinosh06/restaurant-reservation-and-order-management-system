using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
// Alias: "Customer" would otherwise resolve to the Pages.Customer folder.
using CustomerAccount = RestaurantReservation.Models.Customer;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// Customer self-registration. Admin and Staff accounts are not created here.
public class RegisterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<CustomerAccount> _passwordHasher;

    public RegisterModel(ApplicationDbContext db, IPasswordHasher<CustomerAccount> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\+?[0-9\- ]{8,20}$", ErrorMessage = "Please enter a valid phone number.")]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
            ErrorMessage = "Password must contain an uppercase letter, a lowercase letter and a number.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Customer))
        {
            return RedirectToPage("/Customer/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim().ToLowerInvariant();
        if (await _db.Customers.AnyAsync(c => c.Email == email) || await _db.Admins.AnyAsync(a => a.Email == email))
        {
            ModelState.AddModelError("Input.Email", "An account with this email already exists.");
            return Page();
        }

        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            FullName = Input.FullName.Trim(),
            Email = email,
            PhoneNumber = Input.PhoneNumber.Trim()
        };
        customer.Password = _passwordHasher.HashPassword(customer, Input.Password);

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        SuccessMessage = "Registration successful! You can now log in.";
        return RedirectToPage("/Login");
    }
}
