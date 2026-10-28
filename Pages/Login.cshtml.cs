using System.ComponentModel.DataAnnotations;
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

// One login page for everyone, in steps:
//   1. Email. The account decides the next step.
//   2a. Password, for staff/administrators and customers who have a password (with the CAPTCHA check).
//       A customer who forgot it can ask for a login code instead (step 2b), then set a new password.
//   2b. One-time code sent by email, for customers without a password (e.g. created by a guest checkout)
//       and for emails not in the database yet; a correct code creates their customer account.
// The role comes from the database:
//   Admins table (Administrator / Staff) -> admin portal on admin.localhost
//   Customers table                      -> landing page on the main site
public class LoginModel : PageModel
{
    public const string EmailStep = "email";
    public const string PasswordStep = "password";
    public const string CodeStep = "code";

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _adminHasher;
    private readonly IPasswordHasher<CustomerAccount> _customerHasher;
    private readonly CaptchaService _captcha;
    private readonly AdminSignInTicketService _adminTickets;
    private readonly LoginCodeService _loginCodes;
    private readonly EmailSender _email;

    public LoginModel(ApplicationDbContext db, IPasswordHasher<AdminAccount> adminHasher,
        IPasswordHasher<CustomerAccount> customerHasher, CaptchaService captcha, AdminSignInTicketService adminTickets,
        LoginCodeService loginCodes, EmailSender email)
    {
        _db = db;
        _adminHasher = adminHasher;
        _customerHasher = customerHasher;
        _captcha = captcha;
        _adminTickets = adminTickets;
        _loginCodes = loginCodes;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Which step the page shows.
    public string Step { get; private set; } = EmailStep;

    // Shown on the code step, e.g. "We sent a code to ...".
    public string? CodeNotice { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        // Only used on the password step; checked there, not by the attributes.
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Display(Name = "Characters")]
        public string? CaptchaAnswer { get; set; }

        // Only used on the code step.
        [Display(Name = "Login code")]
        public string? Code { get; set; }
    }

    public IActionResult OnGet()
    {
        // Already logged in (e.g. the Back button after logging in): go to the dashboard, not back to returnUrl.
        if (User.IsInRole(AppRoles.Customer))
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    // GET /Login?handler=Captcha - returns a new CAPTCHA image (called by the login popup).
    public IActionResult OnGetCaptcha()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Content(_captcha.CreateCaptchaSvg(HttpContext.Session), "image/svg+xml");
    }

    // Step 1: Continue button after the email.
    public async Task<IActionResult> OnPostEmailAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = NormalizedEmail();
        var hasPassword = await _db.Admins.AnyAsync(a => a.Email == email)
            || await _db.Customers.AnyAsync(c => c.Email == email && c.Password != null && c.Password != "");

        if (hasPassword)
        {
            Step = PasswordStep;
            return Page();
        }

        return await SendCodeAsync(email);
    }

    // Step 2a: password, after the CAPTCHA popup. returnUrl is the page the customer wanted before
    // being sent here (e.g. /Customer/Profile).
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        Step = PasswordStep;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrEmpty(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "Password is required.");
            return Page();
        }

        // CAPTCHA test first, so bots cannot guess passwords.
        if (!_captcha.Validate(HttpContext.Session, Input.CaptchaAnswer))
        {
            ModelState.AddModelError(string.Empty, "CAPTCHA verification failed. Please try again.");
            return Page();
        }

        var email = NormalizedEmail();

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

            // Staff and administrators continue on the admin subdomain with a one-time ticket.
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

            await SignInCustomerAsync(customer, ProfileSetup.PasswordLogin);
            return RedirectToCustomerPage(returnUrl);
        }

        return InvalidLogin();
    }

    // Step 2b: the 6-digit code from the email.
    public async Task<IActionResult> OnPostCodeAsync(string? returnUrl = null)
    {
        Step = CodeStep;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = NormalizedEmail();

        // Staff and administrators always use their password.
        if (await _db.Admins.AnyAsync(a => a.Email == email))
        {
            Step = PasswordStep;
            return Page();
        }

        switch (_loginCodes.Check(email, Input.Code))
        {
            case LoginCodeService.CheckResult.Wrong:
                ModelState.AddModelError("Input.Code", "That code is not right. Please check the email and try again.");
                return Page();
            case LoginCodeService.CheckResult.TooManyAttempts:
                ModelState.AddModelError("Input.Code", "Too many wrong codes. Please ask for a new code.");
                return Page();
            case LoginCodeService.CheckResult.Expired:
                ModelState.AddModelError("Input.Code", "This code has expired. Please ask for a new code.");
                return Page();
        }

        // The code proves the email is theirs: log in, creating the customer account if it is new.
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email);
        if (customer is null)
        {
            customer = new CustomerAccount { Id = Guid.NewGuid().ToString("N"), Email = email };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
        }

        await SignInCustomerAsync(customer, ProfileSetup.CodeLogin);
        return RedirectToCustomerPage(returnUrl);
    }

    // "Send a new code" on the code step, and "Forgot password? Log in with a code instead" on the password step.
    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = NormalizedEmail();

        // Staff and administrators always use their password.
        if (await _db.Admins.AnyAsync(a => a.Email == email))
        {
            Step = PasswordStep;
            ModelState.AddModelError(string.Empty,
                "Staff and administrator accounts log in with their password. Please ask an administrator to reset it.");
            return Page();
        }

        return await SendCodeAsync(email);
    }

    private async Task<IActionResult> SendCodeAsync(string email)
    {
        Step = CodeStep;
        ModelState.Remove("Input.Code");
        Input.Code = null;

        var code = _loginCodes.Issue(email, out var wait);
        if (code is null)
        {
            CodeNotice = $"A code was sent to {email} a moment ago. You can ask for a new one in {Math.Ceiling(wait.TotalSeconds)} seconds.";
            return Page();
        }

        var minutes = (int)LoginCodeService.Lifetime.TotalMinutes;
        var sent = await _email.SendAsync(
            email,
            $"Your login code: {code}",
            $"Your login code is {code}. It works for {minutes} minutes. If you did not try to log in, you can ignore this email.",
            // Built like an email rather than a web page, so it looks the same in Outlook, Gmail and phone apps:
            // a table sized with attributes, empty rows for spacing (margin and padding are unreliable in email),
            // and <strong> instead of font-weight. Spaces between the digits are left out so the code copies cleanly.
            $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              <tr>
                <td width="24"></td>
                <td style="font-family:Arial,Helvetica,sans-serif;color:#111111">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                    <tr><td height="24"></td></tr>
                    <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:22px;color:#111111"><strong>Your login code</strong></td></tr>
                    <tr><td height="12"></td></tr>
                    <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#111111">Use this code to log in. It works for {minutes} minutes.</td></tr>
                    <tr><td height="16"></td></tr>
                    <tr><td style="font-family:'Courier New',Courier,monospace;font-size:34px;color:#111111"><strong>{code}</strong></td></tr>
                    <tr><td height="16"></td></tr>
                    <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#666666">If you did not try to log in, you can ignore this email.</td></tr>
                    <tr><td height="24"></td></tr>
                  </table>
                </td>
                <td width="24"></td>
              </tr>
            </table>
            """);

        if (!sent)
        {
            Step = EmailStep;
            ModelState.AddModelError(string.Empty, "We couldn't send the login code just now. Please try again in a moment.");
            return Page();
        }

        CodeNotice = $"We sent a 6-digit code to {email}. It works for {minutes} minutes.";
        return Page();
    }

    private string NormalizedEmail() => Input.Email.Trim().ToLowerInvariant();

    // Only local URLs are followed, so the login page cannot be used to redirect to another site.
    private IActionResult RedirectToCustomerPage(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");

    private IActionResult InvalidLogin()
    {
        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }

    // Customers without a name or phone number are then sent to complete their profile (see ProfileSetup).
    private Task SignInCustomerAsync(CustomerAccount customer, string loginMethod) =>
        ProfileSetup.SignInCustomerAsync(HttpContext, customer, loginMethod);

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
