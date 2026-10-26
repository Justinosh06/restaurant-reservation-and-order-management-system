using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using Microsoft.Extensions.Options;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Infrastructure.Email;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Admin.Promotions;

[RequestFormLimits(MultipartBodyLengthLimit = ImageUploadService.MaxRequestBytes)]
[AdministratorChangesOnly]
public class PromotionModel : PageModel
{
    private const string UploadFolder = "uploads/promotions";

    private readonly ApplicationDbContext _db;
    private readonly ImageUploadService _images;
    private readonly RestaurantClock _clock;
    private readonly RestaurantCurrency _restaurantCurrency;
    private readonly PromotionEmailSignal _emailSignal;

    public PromotionModel(
        ApplicationDbContext db,
        ImageUploadService images,
        RestaurantClock clock,
        RestaurantCurrency restaurantCurrency,
        PromotionEmailSignal emailSignal,
        IOptions<EmailSettings> emailSettings)
    {
        _db = db;
        _images = images;
        _clock = clock;
        _restaurantCurrency = restaurantCurrency;
        _emailSignal = emailSignal;
        EmailsEnabled = emailSettings.Value.Enabled;
    }

    // Lets the page tell the admin when customers will be emailed about a promotion (PromotionEmailWorker).
    public bool EmailsEnabled { get; }

    // Generated codes leave out 0, O, 1 and I, which are easy to misread.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int GeneratedCodeLength = 8;

    public record PromotionIdRequest(string Id);

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public string CurrencySymbol => Currency.GetSymbol();

    public async Task OnGetAsync()
    {
        Currency = await _restaurantCurrency.GetAsync();
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        var promotions = await _db.Promotions.AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .ThenBy(p => p.Title)
            .ToListAsync();

        return new JsonResult(promotions.Select(ToDto));
    }

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string? startDate,
        [FromForm] string? endDate,
        [FromForm] string? code,
        [FromForm] string? discountType,
        [FromForm] string? discountValue,
        [FromForm] string? minimumSpend,
        IFormFile? image)
    {
        var promotion = new Promotion();
        var error = Apply(promotion, title, description, startDate, endDate)
            ?? ApplyDiscount(
                promotion, discountType, discountValue, minimumSpend,
                (await _restaurantCurrency.GetAsync()).GetSymbol())
            ?? await ApplyCodeAsync(promotion, code);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        if (image is { Length: > 0 })
        {
            var imageError = await ImageUploadService.ValidateAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            promotion.ImageUrl = await _images.SaveAsync(image, UploadFolder);
        }

        _db.Promotions.Add(promotion);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _images.Delete(promotion.ImageUrl, UploadFolder);
            throw;
        }

        // Emails customers now if it has started, or on its start date if it is scheduled.
        _emailSignal.Wake(SiteHosts.MainSiteUrl(Request, "/Customer/Promotions"));
        return new JsonResult(ToDto(promotion));
    }

    // A new image replaces the old one; removeImage clears it. Without either, the current image is kept.
    public async Task<IActionResult> OnPostUpdateAsync(
        [FromForm] string? id,
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string? startDate,
        [FromForm] string? endDate,
        [FromForm] string? code,
        [FromForm] string? discountType,
        [FromForm] string? discountValue,
        [FromForm] string? minimumSpend,
        [FromForm] bool removeImage,
        IFormFile? image)
    {
        var promotion = await _db.Promotions.FindAsync(id);
        if (promotion is null)
        {
            return NotFound(new { error = "This promotion no longer exists. Refresh the page." });
        }

        var error = Apply(promotion, title, description, startDate, endDate)
            ?? ApplyDiscount(
                promotion, discountType, discountValue, minimumSpend,
                (await _restaurantCurrency.GetAsync()).GetSymbol())
            ?? await ApplyCodeAsync(promotion, code);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        var oldImageUrl = promotion.ImageUrl;
        if (image is { Length: > 0 })
        {
            var imageError = await ImageUploadService.ValidateAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            promotion.ImageUrl = await _images.SaveAsync(image, UploadFolder);
        }
        else if (removeImage)
        {
            promotion.ImageUrl = null;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (promotion.ImageUrl != oldImageUrl)
            {
                _images.Delete(promotion.ImageUrl, UploadFolder);
            }
            throw;
        }

        if (promotion.ImageUrl != oldImageUrl)
        {
            _images.Delete(oldImageUrl, UploadFolder);
        }

        // A scheduled promotion moved to start today is emailed now; one already emailed is not sent again.
        _emailSignal.Wake(SiteHosts.MainSiteUrl(Request, "/Customer/Promotions"));
        return new JsonResult(ToDto(promotion));
    }

    // Copies the form values onto the promotion. Returns an error message, or null when everything is valid.
    private static string? Apply(Promotion promotion, string? title, string? description, string? startDate, string? endDate)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        var trimmedDescription = description?.Trim() ?? string.Empty;

        var error = ModelRules.FirstError(new Promotion { Title = trimmedTitle, Description = trimmedDescription },
            nameof(Promotion.Title), nameof(Promotion.Description));
        if (error is not null)
        {
            return error;
        }
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var end))
        {
            return "Please enter a valid start and end date.";
        }
        if (end < start)
        {
            return "The end date must be on or after the start date.";
        }

        promotion.Title = trimmedTitle;
        promotion.Description = trimmedDescription;
        promotion.StartDate = start;
        promotion.EndDate = end;
        return null;
    }

    // Sets a percentage or a fixed discount and an optional minimum spend, all monetary values in the restaurant currency.
    private static string? ApplyDiscount(
        Promotion promotion,
        string? discountType,
        string? discountValue,
        string? minimumSpend,
        string currencySymbol)
    {
        if (!Enum.TryParse<PromotionDiscountType>(discountType, out var type) || !Enum.IsDefined(type))
        {
            return "Please choose the kind of discount the promo code gives.";
        }
        if (!decimal.TryParse(discountValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return "Please enter a discount greater than zero.";
        }

        int amount;
        if (type == PromotionDiscountType.Percentage)
        {
            if (value != decimal.Truncate(value) || value > 100)
            {
                return "A percentage discount must be a whole number from 1 to 100.";
            }
            amount = (int)value;
        }
        else
        {
            if (decimal.Round(value, 2) != value || value > 100000)
            {
                return $"Please enter the discount in {currencySymbol} with at most 2 decimal places.";
            }
            amount = (int)(value * 100);
        }

        var minimum = 0m;
        if (!string.IsNullOrWhiteSpace(minimumSpend) &&
            (!decimal.TryParse(minimumSpend, NumberStyles.Number, CultureInfo.InvariantCulture, out minimum) ||
             minimum < 0 || decimal.Round(minimum, 2) != minimum || minimum > 100000))
        {
            return $"Please enter the minimum spend in {currencySymbol} with at most 2 decimal places, or leave it empty.";
        }

        promotion.DiscountType = type;
        promotion.DiscountValue = amount;
        promotion.MinimumSpend = (int)(minimum * 100);
        return null;
    }

    // Sets the promo code: the one typed in (upper-cased, spaces removed), or a new random one when left blank.
    // Returns an error message, or null when the code is valid and no other promotion uses it.
    private async Task<string?> ApplyCodeAsync(Promotion promotion, string? code)
    {
        var typed = string.Concat((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (typed.Length == 0)
        {
            do
            {
                typed = new string(Random.Shared.GetItems(CodeAlphabet.AsSpan(), GeneratedCodeLength));
            }
            while (await CodeTakenAsync(typed, promotion.Id));
        }
        else
        {
            var error = ModelRules.FirstError(new Promotion { Code = typed }, nameof(Promotion.Code));
            if (error is not null)
            {
                return error;
            }
            if (await CodeTakenAsync(typed, promotion.Id))
            {
                return $"Another promotion already uses the code {typed}.";
            }
        }

        promotion.Code = typed;
        return null;
    }

    private Task<bool> CodeTakenAsync(string code, string id) =>
        _db.Promotions.AnyAsync(p => p.Code == code && p.Id != id);

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] PromotionIdRequest request)
    {
        var promotion = await _db.Promotions.FindAsync(request.Id);
        if (promotion is null)
        {
            return NotFound(new { error = "This promotion no longer exists. Refresh the page." });
        }

        _db.Promotions.Remove(promotion);
        await _db.SaveChangesAsync();
        _images.Delete(promotion.ImageUrl, UploadFolder);

        return new NoContentResult();
    }

    private object ToDto(Promotion p) => new
    {
        id = p.Id,
        status = p.EndDate < _clock.Today ? "Ended" : p.StartDate > _clock.Today ? "Scheduled" : "Active",
        title = p.Title,
        code = p.Code,
        description = p.Description,
        imageUrl = p.ImageUrl,
        startDate = p.StartDate.ToString("yyyy-MM-dd"),
        endDate = p.EndDate.ToString("yyyy-MM-dd"),
        discountType = p.DiscountType?.ToString(),
        // Percent for Percentage; the restaurant currency for FixedAmount and the minimum spend.
        discountValue = p.DiscountType == PromotionDiscountType.FixedAmount ? p.DiscountValue / 100m : p.DiscountValue,
        minimumSpend = p.MinimumSpend / 100m
    };
}
