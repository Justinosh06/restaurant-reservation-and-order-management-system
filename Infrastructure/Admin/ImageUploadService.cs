using System.Text;

namespace RestaurantReservation.Infrastructure.Admin;

// Checks and stores images uploaded from the admin portal (promotions and menu items).
// Each feature keeps its files in its own folder under wwwroot/uploads, e.g. "uploads/menu".
public class ImageUploadService
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    // Leaves room for the other form fields sent with the image. Used in [RequestFormLimits] on upload pages.
    public const int MaxRequestBytes = MaxImageBytes + 1024 * 1024;

    public static readonly Dictionary<string, string> AllowedImageTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public static string AcceptAttribute => string.Join(", ", AllowedImageTypes.Keys);

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImageUploadService> _logger;

    public ImageUploadService(IWebHostEnvironment env, ILogger<ImageUploadService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // Returns an error message, or null when the file is an image that can be saved.
    public static async Task<string?> ValidateAsync(IFormFile image)
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

    // Saves a validated image under a new random name and returns its URL, e.g. "/uploads/menu/3f2a....jpg".
    public async Task<string> SaveAsync(IFormFile image, string folder)
    {
        var folderPath = Path.Combine(_env.WebRootPath, folder);
        Directory.CreateDirectory(folderPath);

        var fileName = Guid.NewGuid().ToString("N") + AllowedImageTypes[image.ContentType];
        await using (var output = File.Create(Path.Combine(folderPath, fileName)))
        {
            await image.CopyToAsync(output);
        }
        return $"/{folder}/{fileName}";
    }

    // Only deletes files this service saved in that folder, so images entered another way are left alone.
    public void Delete(string? imageUrl, string folder)
    {
        if (string.IsNullOrEmpty(imageUrl) || !imageUrl.StartsWith($"/{folder}/", StringComparison.Ordinal))
        {
            return;
        }

        var path = Path.Combine(_env.WebRootPath, folder, Path.GetFileName(imageUrl));
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete uploaded image {Path}", path);
        }
    }
}
