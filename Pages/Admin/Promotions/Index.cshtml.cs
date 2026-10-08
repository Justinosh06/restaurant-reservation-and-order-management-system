using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;

namespace RestaurantReservation.Pages.Admin.Promotions;

[RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 1024 * 1024)]
public class IndexModel : PageModel
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

    public IndexModel(ApplicationDbContext db, IWebHostEnvironment env, RestaurantClock clock)
    {
        _db = db;
        _env = env;
        _clock = clock;
    }

    public record PromotionIdRequest(string Id);

    public void OnGet()
    {
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
        IFormFile? image)
    {
        var promotion = new Promotion
        {
            Title = title?.Trim() ?? string.Empty,
            Description = description?.Trim() ?? string.Empty
        };

        var error = ModelRules.FirstError(promotion, nameof(Promotion.Title), nameof(Promotion.Description));
        if (error is not null)
        {
            return BadRequest(new { error });
        }
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var start) ||
            !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var end))
        {
            return BadRequest(new { error = "Please enter a valid start and end date." });
        }
        if (end < start)
        {
            return BadRequest(new { error = "The end date must be on or after the start date." });
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

        promotion.StartDate = start;
        promotion.EndDate = end;

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
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (!MatchesSignature(image.ContentType, header.AsSpan(0, read)))
        {
            return "This file isn't a valid JPG, PNG or WebP image.";
        }
        return null;
    }

    private static bool MatchesSignature(string contentType, ReadOnlySpan<byte> header) => contentType switch
    {
        "image/jpeg" => header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };

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
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
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
