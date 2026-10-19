using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Infrastructure.Admin;

public class RestaurantCurrency
{
    private readonly ApplicationDbContext _db;

    public RestaurantCurrency(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CurrencyType> GetAsync() =>
        await _db.RestaurantSettings.AsNoTracking()
            .Select(s => (CurrencyType?)s.DefaultCurrency)
            .FirstOrDefaultAsync()
        ?? CurrencyType.MYR;
}
