using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Pages.Admin.Settings;

public class SettingModel : PageModel
{
    private static readonly TimeOnly DefaultOpenTime = new(11, 0);
    private static readonly TimeOnly DefaultCloseTime = new(22, 0);

    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private readonly ApplicationDbContext _db;

    public SettingModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IEnumerable<(string Value, string Label)> CurrencyOptions =>
        Enum.GetValues<CurrencyType>().Select(c => (c.ToString(), $"{c.GetSymbol()} ({c.GetCode()})"));

    public record BusinessHoursRequest(int DayOfWeek, string? OpenTime, string? CloseTime, bool IsClosed);

    public record SaveSettingsRequest(
        string? Name,
        string? Address,
        string? PhoneNumber,
        string? DefaultCurrency,
        List<BusinessHoursRequest>? BusinessHours);

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetSettingsAsync()
    {
        var settings = await _db.RestaurantSettings.AsNoTracking()
            .Include(s => s.BusinessHours)
            .FirstOrDefaultAsync();

        return new JsonResult(ToDto(settings));
    }

    public async Task<IActionResult> OnPostSaveAsync([FromBody] SaveSettingsRequest request)
    {
        var details = new RestaurantSettings
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim()
        };

        var detailsError = ModelRules.FirstError(details,
            nameof(RestaurantSettings.Name), nameof(RestaurantSettings.Address), nameof(RestaurantSettings.PhoneNumber));
        if (detailsError is not null)
        {
            return BadRequest(new { error = detailsError });
        }
        if (!Enum.TryParse<CurrencyType>(request.DefaultCurrency, out var currency) || !Enum.IsDefined(currency))
        {
            return BadRequest(new { error = "Please choose a valid currency." });
        }

        var hoursResult = ParseHours(request.BusinessHours);
        if (hoursResult.Error is not null)
        {
            return BadRequest(new { error = hoursResult.Error });
        }

        var settings = await _db.RestaurantSettings
            .Include(s => s.BusinessHours)
            .FirstOrDefaultAsync();

        if (settings is null)
        {
            settings = new RestaurantSettings { Id = Guid.NewGuid().ToString("N") };
            _db.RestaurantSettings.Add(settings);
        }

        settings.Name = details.Name;
        settings.Address = details.Address;
        settings.PhoneNumber = details.PhoneNumber;
        settings.DefaultCurrency = currency;
        settings.UpdatedAt = DateTime.UtcNow;

        foreach (var parsed in hoursResult.Hours)
        {
            var existing = settings.BusinessHours.FirstOrDefault(h => h.DayOfWeek == parsed.DayOfWeek);
            if (existing is null)
            {
                existing = new BusinessHours
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DayOfWeek = parsed.DayOfWeek,
                    RestaurantSettingsId = settings.Id
                };
                settings.BusinessHours.Add(existing);
            }

            existing.IsClosed = parsed.IsClosed;
            existing.OpenTime = parsed.OpenTime;
            existing.CloseTime = parsed.CloseTime;
        }

        await _db.SaveChangesAsync();

        return new JsonResult(ToDto(settings));
    }

    private static (List<BusinessHours> Hours, string? Error) ParseHours(List<BusinessHoursRequest>? requested)
    {
        if (requested is null || requested.Count != 7 ||
            requested.Select(h => h.DayOfWeek).Distinct().Count() != 7 ||
            requested.Any(h => !Enum.IsDefined((DayOfWeek)h.DayOfWeek)))
        {
            return ([], "Operating hours must include each day of the week once.");
        }

        var hours = new List<BusinessHours>();
        var invalidDays = new List<string>();

        foreach (var day in requested)
        {
            var dayOfWeek = (DayOfWeek)day.DayOfWeek;
            if (day.IsClosed)
            {
                hours.Add(new BusinessHours { DayOfWeek = dayOfWeek, IsClosed = true });
                continue;
            }

            if (!TimeOnly.TryParse(day.OpenTime, out var open) ||
                !TimeOnly.TryParse(day.CloseTime, out var close) ||
                close <= open)
            {
                invalidDays.Add(dayOfWeek.ToString());
                continue;
            }

            hours.Add(new BusinessHours { DayOfWeek = dayOfWeek, IsClosed = false, OpenTime = open, CloseTime = close });
        }

        return invalidDays.Count > 0
            ? ([], $"Closing time must be after opening time for: {string.Join(", ", invalidDays)}.")
            : (hours, null);
    }

    private static object ToDto(RestaurantSettings? settings) => new
    {
        exists = settings is not null,
        name = settings?.Name ?? string.Empty,
        address = settings?.Address ?? string.Empty,
        phoneNumber = settings?.PhoneNumber,
        defaultCurrency = (settings?.DefaultCurrency ?? CurrencyType.MYR).ToString(),
        updatedAt = settings?.UpdatedAt,
        businessHours = WeekOrder.Select(day =>
        {
            var saved = settings?.BusinessHours.FirstOrDefault(h => h.DayOfWeek == day);
            return new
            {
                dayOfWeek = (int)day,
                openTime = (saved is null ? DefaultOpenTime : saved.OpenTime)?.ToString("HH:mm"),
                closeTime = (saved is null ? DefaultCloseTime : saved.CloseTime)?.ToString("HH:mm"),
                isClosed = saved?.IsClosed ?? false
            };
        })
    };
}
