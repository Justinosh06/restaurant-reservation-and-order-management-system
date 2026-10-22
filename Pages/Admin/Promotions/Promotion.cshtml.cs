using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Promotions;

[RequestFormLimits(MultipartBodyLengthLimit = ImageUploadService.MaxRequestBytes)]
public class PromotionModel : PageModel
{
    private const string UploadFolder = "uploads/promotions";

    private readonly ApplicationDbContext _db;
    private readonly ImageUploadService _images;
    private readonly RestaurantClock _clock;

    public PromotionModel(ApplicationDbContext db, ImageUploadService images, RestaurantClock clock)
    {
        _db = db;
        _images = images;
        _clock = clock;
    }

    public record PromotionIdRequest(string Id);

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
        IFormFile? image)
    {
        var promotion = new Promotion();
        var error = Apply(promotion, title, description, startDate, endDate);
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

        return new JsonResult(ToDto(promotion));
    }

    // A new image replaces the old one; removeImage clears it. Without either, the current image is kept.
    public async Task<IActionResult> OnPostUpdateAsync(
        [FromForm] string? id,
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string? startDate,
        [FromForm] string? endDate,
        [FromForm] bool removeImage,
        IFormFile? image)
    {
        var promotion = await _db.Promotions.FindAsync(id);
        if (promotion is null)
        {
            return NotFound(new { error = "This promotion no longer exists. Refresh the page." });
        }

        var error = Apply(promotion, title, description, startDate, endDate);
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
        description = p.Description,
        imageUrl = p.ImageUrl,
        startDate = p.StartDate.ToString("yyyy-MM-dd"),
        endDate = p.EndDate.ToString("yyyy-MM-dd")
    };
}
