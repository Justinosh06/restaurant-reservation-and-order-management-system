using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Promotions;

[RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 1024 * 1024)]
public class PromotionModel : PageModel
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    private const string UploadFolder = "uploads/promotions";

    public static readonly Dictionary<string, string> AllowedImageTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly RestaurantClock _clock;
    private readonly ILogger<PromotionModel> _logger;

    public PromotionModel(ApplicationDbContext db, IWebHostEnvironment env, RestaurantClock clock, ILogger<PromotionModel> logger)
    {
        _db = db;
        _env = env;
        _clock = clock;
        _logger = logger;
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
            var imageError = await ValidateImageAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            promotion.ImageUrl = await SaveImageAsync(image);
        }

        _db.Promotions.Add(promotion);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            DeleteImage(promotion.ImageUrl);
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
            var imageError = await ValidateImageAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            promotion.ImageUrl = await SaveImageAsync(image);
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
                DeleteImage(promotion.ImageUrl);
            }
            throw;
        }

        if (promotion.ImageUrl != oldImageUrl)
        {
            DeleteImage(oldImageUrl);
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
        DeleteImage(promotion.ImageUrl);

        return new NoContentResult();
    }

    private static async Task<string?> ValidateImageAsync(IFormFile image)
    {
        if (image.Length > MaxImageBytes)
        {
            return $"Image must be {MaxImageBytes / (1024 * 1024)} MB or smaller.";
        }
        if (!AllowedImageTypes.ContainsKey(image.ContentType))
        {
            return "Please choose a JPG, PNG or WebP image.";
        }

        var header = new byte[12];
        await using var stream = image.OpenReadStream();
        await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (!LooksLikeImage(image.ContentType, header))
        {
            return "This file isn't a valid JPG, PNG or WebP image.";
        }
        return null;
    }

    // Checks the first bytes of the file, so a renamed non-image file is rejected.
    private static bool LooksLikeImage(string contentType, byte[] header)
    {
        if (contentType == "image/jpeg")
        {
            return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }
        if (contentType == "image/png")
        {
            byte[] pngStart = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return header.Take(8).SequenceEqual(pngStart);
        }
        if (contentType == "image/webp")
        {
            return Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
        }
        return false;
    }

    private async Task<string> SaveImageAsync(IFormFile image)
    {
        var folder = Path.Combine(_env.WebRootPath, UploadFolder);
        Directory.CreateDirectory(folder);

        var fileName = Guid.NewGuid().ToString("N") + AllowedImageTypes[image.ContentType];
        await using (var output = System.IO.File.Create(Path.Combine(folder, fileName)))
        {
            await image.CopyToAsync(output);
        }
        return $"/{UploadFolder}/{fileName}";
    }

    private void DeleteImage(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl) || !imageUrl.StartsWith($"/{UploadFolder}/", StringComparison.Ordinal))
        {
            return;
        }

        var path = Path.Combine(_env.WebRootPath, UploadFolder, Path.GetFileName(imageUrl));
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete promotion image {Path}", path);
        }
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
