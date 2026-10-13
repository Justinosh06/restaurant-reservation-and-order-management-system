using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

// Customer profile. Only customers can open this page (see AuthorizeFolder in Program.cs).
// User ID and Email are shown but can never be changed.
public class ProfileModel : PageModel
{
    private const int HistorySize = 10;

    private readonly ApplicationDbContext _db;

    public ProfileModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string RestaurantName { get; private set; } = "Resto";
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;

    // The latest orders, each with its dining session and ordered items.
    public IReadOnlyList<Order> Orders { get; private set; } = [];
    public ILookup<string, OrderItem> OrderItems { get; private set; } = Enumerable.Empty<OrderItem>().ToLookup(i => i.OrderId);

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
            PhoneNumber = customer.PhoneNumber ?? string.Empty
        };
        IsEditing = edit;
        await LoadPageDataAsync();
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

        if (!ModelState.IsValid)
        {
            IsEditing = true;
            await LoadPageDataAsync();
            return Page();
        }

        customer.FirstName = Input.FirstName.Trim();
        customer.LastName = Input.LastName.Trim();
        customer.PhoneNumber = Input.PhoneNumber.Trim();
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

    // Everything on the page besides the profile form: the restaurant name in the navigation bar and the history lists.
    private async Task LoadPageDataAsync()
    {
        var settings = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is not null)
        {
            RestaurantName = settings.Name;
            Currency = settings.DefaultCurrency;
        }

        // Sample history for now; switch to LoadHistoryAsync once order processing saves real orders.
        Orders = SampleOrders.All;
        OrderItems = SampleOrders.Items;
    }

    private async Task LoadHistoryAsync()
    {
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        Orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(HistorySize)
            .ToListAsync();

        var orderIds = Orders.Select(o => o.Id).ToList();
        var items = await _db.OrderItems
            .AsNoTracking()
            .Include(i => i.MenuItem)
            .Where(i => orderIds.Contains(i.OrderId))
            .ToListAsync();
        OrderItems = items.ToLookup(i => i.OrderId);
    }
}
