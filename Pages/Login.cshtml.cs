using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Services;
// Aliases: "Admin" and "Customer" would otherwise resolve to the Pages.Admin / Pages.Customer folders.
using AdminAccount = RestaurantReservation.Models.Admin;
using CustomerAccount = RestaurantReservation.Models.Customer;

namespace Restaurant_Reservation_and_Order_Management_System.Pages;

// One login page for everyone. The role comes from the database:
//   Admins table (Administrator / Staff) -> admin portal on admin.localhost
//   Customers table                      -> /Customer on the main site
public class LoginModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _adminHasher;
    private readonly IPasswordHasher<CustomerAccount> _customerHasher;
    private readonly CaptchaService _captcha;
    private readonly AdminSignInTicketService _adminTickets;

    public LoginModel(ApplicationDbContext db, IPasswordHasher<AdminAccount> adminHasher,
        IPasswordHasher<CustomerAccount> customerHasher, CaptchaService captcha, AdminSignInTicketService adminTickets)
    {
        _db = db;
        _adminHasher = adminHasher;
        _customerHasher = customerHasher;
        _captcha = captcha;
        _adminTickets = adminTickets;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Characters")]
        public string? CaptchaAnswer { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Customer))
        {
            return RedirectToPage("/Customer/Index");
        }

        return Page();
    }

    // GET /Login?handler=Captcha - returns a new CAPTCHA image (called by the login popup).
    public IActionResult OnGetCaptcha()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Content(_captcha.CreateCaptchaSvg(HttpContext.Session), "image/svg+xml");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Step 1: CAPTCHA test (checked before the password so bots cannot guess passwords).
        if (!_captcha.Validate(HttpContext.Session, Input.CaptchaAnswer))
        {
            ModelState.AddModelError(string.Empty, "CAPTCHA verification failed. Please try again.");
            return Page();
        }

        // Step 2: find the account by email and check the password.
        var email = Input.Email.Trim().ToLowerInvariant();

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == email);
        if (admin is not null)
        {
            if (!CheckPassword(_adminHasher, admin, admin.Password, Input.Password, out var newHash))
            {
                return InvalidLogin();
            }
            if (newHash is not null)
            {
                admin.Password = newHash;
                await _db.SaveChangesAsync();
            }

            // Step 3 (Admin/Staff): continue on the admin subdomain with a one-time ticket.
            var ticket = _adminTickets.Create(admin.Id);
            return Redirect(SiteHosts.AdminSiteUrl(Request, $"/Admin/SignIn?ticket={Uri.EscapeDataString(ticket)}"));
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email);
        if (customer is not null)
        {
            if (!CheckPassword(_customerHasher, customer, customer.Password, Input.Password, out var newHash))
            {
                return InvalidLogin();
            }
            if (newHash is not null)
            {
                customer.Password = newHash;
                await _db.SaveChangesAsync();
            }

            // Step 3 (Customer): sign in on the main site.
            var name = string.IsNullOrWhiteSpace(customer.FullName) ? customer.Email : customer.FullName;
            await SignInAsync(customer.Id, name, customer.Email, AppRoles.Customer);
            return RedirectToPage("/Customer/Index");
        }

        return InvalidLogin();
    }

    private IActionResult InvalidLogin()
    {
        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }

    private Task SignInAsync(string id, string name, string email, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    // newHash is set when the stored hash uses old settings and should be replaced.
    private static bool CheckPassword<TUser>(IPasswordHasher<TUser> hasher, TUser user, string? storedHash,
        string password, out string? newHash) where TUser : class
    {
        newHash = null;
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        PasswordVerificationResult result;
        try
        {
            result = hasher.VerifyHashedPassword(user, storedHash, password);
        }
        catch (FormatException)
        {
            // The stored value is not a password hash (e.g. typed into the table by hand).
            return false;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            newHash = hasher.HashPassword(user, password);
        }

        return result != PasswordVerificationResult.Failed;
    }
}
