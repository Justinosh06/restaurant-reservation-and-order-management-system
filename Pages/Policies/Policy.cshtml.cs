using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Reservations;
using Restaurant_Reservation_and_Order_Management_System.Pages.Admin;
using Restaurant_Reservation_and_Order_Management_System.Pages.Customer;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Policies;

// The site's policies (/Policies/privacy, /Policies/terms, /Policies/reservations), open to everyone.
// The wording is in Content/Policies/*.md, so it can be edited without touching code. {{Placeholders}}
// in it are filled from the restaurant's settings and the booking rules in code, so the policy always
// says what the site actually does (the deposit amount, the cancel deadline, ...).
public class PolicyModel(IWebHostEnvironment environment, ApplicationDbContext db, RestaurantCurrency restaurantCurrency)
    : PageModel
{
    // Address name → title, in the order they are listed in the page's tabs and the footers.
    public static readonly (string Name, string Title)[] Policies =
    [
        ("privacy", "Privacy policy"),
        ("terms", "Terms of use"),
        ("reservations", "Reservation policy")
    ];

    public string Name { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string RestaurantName { get; private set; } = "Resto";

    public HtmlString Body { get; private set; } = HtmlString.Empty;

    public async Task<IActionResult> OnGetAsync(string? name)
    {
        // Only the names listed above, so the address can't be used to read any other file.
        var policy = Policies.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (policy.Name is null)
        {
            return NotFound();
        }

        var path = Path.Combine(environment.ContentRootPath, "Content", "Policies", $"{policy.Name}.md");
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var settings = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        RestaurantName = string.IsNullOrWhiteSpace(settings?.Name) ? RestaurantName : settings.Name;
        var phone = settings?.PhoneNumber;
        var address = settings?.Address;
        var contact = new[] { string.IsNullOrWhiteSpace(phone) ? null : $"by phone on {phone}",
                              string.IsNullOrWhiteSpace(address) ? null : $"at {address}" }
            .Where(part => part is not null)
            .ToList();

        var values = new Dictionary<string, string>
        {
            ["RestaurantName"] = RestaurantName,
            ["Deposit"] = ReservationSummary.DepositText((int)(ReserveModel.Deposit * 100), await restaurantCurrency.GetAsync()),
            ["MinDaysAhead"] = ReserveModel.MinDaysAhead.ToString(),
            ["DaysAhead"] = ReserveModel.DaysAhead.ToString(),
            ["MaxPartySize"] = ReserveModel.MaxPartySize.ToString(),
            ["SlotHours"] = ReservationSummary.SlotLength.TotalHours.ToString("0"),
            ["CancelHours"] = ReservationCancellation.Notice.TotalHours.ToString("0"),
            // " on 03-1234 5678" or "", so the sentence reads well either way.
            ["PhoneSentence"] = string.IsNullOrWhiteSpace(phone) ? "" : $" on {phone}",
            ["ContactSentence"] = contact.Count == 0 ? " at the restaurant" : " " + string.Join(" or ", contact)
        };

        var markdown = await System.IO.File.ReadAllTextAsync(path);
        foreach (var (key, value) in values)
        {
            markdown = markdown.Replace("{{" + key + "}}", value);
        }

        Name = policy.Name;
        Title = policy.Title;
        Body = markdown.ToHtml();
        return Page();
    }
}
