using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Pages.Admin.Menu;

[RequestFormLimits(MultipartBodyLengthLimit = ImageUploadService.MaxRequestBytes)]
public class MenuModel : PageModel
{
    public const int MaxOptions = 6;
    public const int MaxOptionNameLength = 40;
    public const int MaxChoices = 10;
    public const decimal MaxPrice = 100_000m;

    private const string UploadFolder = "uploads/menu";

    private readonly ApplicationDbContext _db;
    private readonly ImageUploadService _images;

    public MenuModel(ApplicationDbContext db, ImageUploadService images)
    {
        _db = db;
        _images = images;
    }

    public static IReadOnlyList<MenuItemType> Categories { get; } = Enum.GetValues<MenuItemType>();

    // The customer menu page on the main site (the admin portal runs on the admin subdomain).
    public string PreviewUrl => SiteHosts.MainSiteUrl(Request, "/Menu");

    public record MenuItemIdRequest(string Id);

    // An option the customer picks when ordering, e.g. Name "Size" with Choices ["Regular", "Large"].
    public record OptionInput(string? Name, List<string>? Choices);

    public async Task<IActionResult> OnGetListAsync()
    {
        // Sorted after loading: the category is stored as text, so the database would sort it alphabetically
        // instead of in serving order (Starter, Salad, ... Beverage).
        var items = (await _db.MenuItems.AsNoTracking().ToListAsync())
            .OrderBy(m => m.MenuItemType)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderCounts = await _db.OrderItems.AsNoTracking()
            .GroupBy(oi => oi.MenuItemId)
            .Select(g => new { MenuItemId = g.Key, Quantity = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(g => g.MenuItemId, g => g.Quantity);

        return new JsonResult(items.Select(m => ToDto(m, orderCounts.GetValueOrDefault(m.Id))));
    }

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string? name,
        [FromForm] string? description,
        [FromForm] string? category,
        [FromForm] string? price,
        [FromForm] bool available,
        [FromForm] string? options,
        IFormFile? image)
    {
        var item = new MenuItem { Id = Guid.NewGuid().ToString("N") };
        var error = Apply(item, name, description, category, price, available, options);
        if (error is not null)
        {
            return BadRequest(new { error });
        }
        if (await NameTakenAsync(item.Name, item.Id))
        {
            return BadRequest(new { error = "Another menu item already has this name." });
        }

        if (image is { Length: > 0 })
        {
            var imageError = await ImageUploadService.ValidateAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            item.ThumbnailImage = await _images.SaveAsync(image, UploadFolder);
        }

        _db.MenuItems.Add(item);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _images.Delete(item.ThumbnailImage, UploadFolder);
            throw;
        }

        return new JsonResult(ToDto(item, 0));
    }

    // A new image replaces the old one; removeImage clears it. Without either, the current image is kept.
    public async Task<IActionResult> OnPostUpdateAsync(
        [FromForm] string? id,
        [FromForm] string? name,
        [FromForm] string? description,
        [FromForm] string? category,
        [FromForm] string? price,
        [FromForm] bool available,
        [FromForm] string? options,
        [FromForm] bool removeImage,
        IFormFile? image)
    {
        var item = await _db.MenuItems.FindAsync(id);
        if (item is null)
        {
            return NotFound(new { error = "This menu item no longer exists. Refresh the page." });
        }

        var error = Apply(item, name, description, category, price, available, options);
        if (error is not null)
        {
            return BadRequest(new { error });
        }
        if (await NameTakenAsync(item.Name, item.Id))
        {
            return BadRequest(new { error = "Another menu item already has this name." });
        }

        var oldImage = item.ThumbnailImage;
        if (image is { Length: > 0 })
        {
            var imageError = await ImageUploadService.ValidateAsync(image);
            if (imageError is not null)
            {
                return BadRequest(new { error = imageError });
            }
            item.ThumbnailImage = await _images.SaveAsync(image, UploadFolder);
        }
        else if (removeImage)
        {
            item.ThumbnailImage = null;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (item.ThumbnailImage != oldImage)
            {
                _images.Delete(item.ThumbnailImage, UploadFolder);
            }
            throw;
        }

        if (item.ThumbnailImage != oldImage)
        {
            _images.Delete(oldImage, UploadFolder);
        }

        return new JsonResult(ToDto(item, await TimesOrderedAsync(item.Id)));
    }

    public async Task<IActionResult> OnPostToggleAsync([FromBody] MenuItemIdRequest request)
    {
        var item = await _db.MenuItems.FindAsync(request.Id);
        if (item is null)
        {
            return NotFound(new { error = "This menu item no longer exists. Refresh the page." });
        }

        item.Availability = !item.Availability;
        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(item, await TimesOrderedAsync(item.Id)));
    }

    // Past orders and reviews keep a link to the dish they were for, so a dish that has been ordered or reviewed
    // can only be marked unavailable. Any other dish is removed, along with any copies sitting in carts.
    public async Task<IActionResult> OnPostDeleteAsync([FromBody] MenuItemIdRequest request)
    {
        var item = await _db.MenuItems.FindAsync(request.Id);
        if (item is null)
        {
            return NotFound(new { error = "This menu item no longer exists. Refresh the page." });
        }

        if (await _db.OrderItems.AnyAsync(oi => oi.MenuItemId == item.Id))
        {
            return new ConflictObjectResult(new { error = $"\"{item.Name}\" has been ordered before, so it can't be deleted. Mark it unavailable to hide it from the menu instead." });
        }

        // Deleting a dish also deletes its customer reviews (Feedback cascades), so a reviewed dish is kept too.
        if (await _db.Feedbacks.AnyAsync(f => f.MenuItemId == item.Id))
        {
            return new ConflictObjectResult(new { error = $"\"{item.Name}\" has customer reviews, so it can't be deleted. Mark it unavailable to hide it from the menu instead." });
        }

        _db.CartItems.RemoveRange(_db.CartItems.Where(c => c.MenuItemId == item.Id));
        _db.MenuItems.Remove(item);
        await _db.SaveChangesAsync();
        _images.Delete(item.ThumbnailImage, UploadFolder);

        return new NoContentResult();
    }

    // Copies the form values onto the menu item. Returns an error message, or null when everything is valid.
    private static string? Apply(MenuItem item, string? name, string? description, string? category, string? price,
        bool available, string? optionsJson)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedDescription = description?.Trim() ?? string.Empty;
        var error = ModelRules.FirstError(new MenuItem { Name = trimmedName, Description = trimmedDescription },
            nameof(MenuItem.Name), nameof(MenuItem.Description));
        if (error is not null)
        {
            return error;
        }

        if (!Enum.TryParse<MenuItemType>(category, out var type) || !Enum.IsDefined(type))
        {
            return "Please choose one of the listed categories.";
        }

        if (!decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            return "Please enter a valid price.";
        }
        if (amount > MaxPrice)
        {
            return $"Price can't be more than {MaxPrice:N0}.";
        }
        if (decimal.Round(amount, 2) != amount)
        {
            return "Price can have at most 2 decimal places.";
        }

        var optionsError = ParseOptions(optionsJson, out var options);
        if (optionsError is not null)
        {
            return optionsError;
        }

        item.Name = trimmedName;
        item.Description = trimmedDescription;
        item.MenuItemType = type;
        item.Price = (int)(amount * 100);
        item.Availability = available;
        item.Options = options;
        return null;
    }

    // The options are stored as { "Size": "Regular, Large" }; the table ordering page splits the choices on commas.
    private static string? ParseOptions(string? json, out Dictionary<string, string> options)
    {
        options = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        List<OptionInput>? inputs;
        try
        {
            inputs = JsonSerializer.Deserialize<List<OptionInput>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return "The options couldn't be read. Refresh the page and try again.";
        }

        foreach (var input in inputs ?? [])
        {
            var optionName = input.Name?.Trim() ?? string.Empty;
            var choices = (input.Choices ?? [])
                .Select(c => c?.Trim() ?? string.Empty)
                .Where(c => c.Length > 0)
                .ToList();

            if (optionName.Length == 0 && choices.Count == 0)
            {
                continue;
            }
            if (optionName.Length == 0)
            {
                return "Every option needs a name, e.g. \"Size\".";
            }
            if (optionName.Length > MaxOptionNameLength)
            {
                return $"Option names can't be longer than {MaxOptionNameLength} characters.";
            }
            if (options.Keys.Any(k => string.Equals(k, optionName, StringComparison.OrdinalIgnoreCase)))
            {
                return $"There are two options called \"{optionName}\".";
            }
            if (choices.Count == 0)
            {
                return $"Add at least one choice for \"{optionName}\".";
            }
            if (choices.Count > MaxChoices)
            {
                return $"\"{optionName}\" can have at most {MaxChoices} choices.";
            }
            if (choices.Any(c => c.Contains(',')))
            {
                return "Choices can't contain commas.";
            }
            if (choices.Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Count)
            {
                return $"\"{optionName}\" has the same choice twice.";
            }

            options[optionName] = string.Join(", ", choices);
        }

        return options.Count > MaxOptions ? $"A menu item can have at most {MaxOptions} options." : null;
    }

    private Task<bool> NameTakenAsync(string name, string id) =>
        _db.MenuItems.AnyAsync(m => m.Name == name && m.Id != id);

    private async Task<int> TimesOrderedAsync(string menuItemId) =>
        await _db.OrderItems.Where(oi => oi.MenuItemId == menuItemId).SumAsync(oi => (int?)oi.Quantity) ?? 0;

    private object ToDto(MenuItem m, int timesOrdered) => new
    {
        id = m.Id,
        name = m.Name,
        description = m.Description ?? string.Empty,
        category = m.MenuItemType.ToString(),
        price = m.Price,
        imageUrl = string.IsNullOrWhiteSpace(m.ThumbnailImage) ? null : Url.Content(m.ThumbnailImage),
        available = m.Availability,
        timesOrdered,
        options = m.Options.Select(o => new
        {
            name = o.Key,
            choices = o.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        })
    };
}
