using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Models;

namespace RestaurantReservation.Data;

public static class DbSeeder
{
    // Creates the database (if needed) and the default Administrator and Staff accounts.
    // Customers are not seeded; they create their own accounts on the Register page.
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Admin>>();

        // Uses EF migrations once the project has them; until then the tables are created directly.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        await AddAdminIfMissingAsync(db, hasher, "admin@restaurant.com", "Admin@123", AdminRole.Administrator);
        await AddAdminIfMissingAsync(db, hasher, "staff@restaurant.com", "Staff@123", AdminRole.Staff);
        await AddDefaultTablesIfMissingAsync(db);

        await db.SaveChangesAsync();
    }

    // Creates the dining-room tables (T1-T16) when there are none yet.
    // CoordinateX/Y are the column and row on the Table Management floor plan.
    private static async Task AddDefaultTablesIfMissingAsync(ApplicationDbContext db)
    {
        if (await db.Tables.AnyAsync())
        {
            return;
        }

        (int Column, int Row, int Capacity)[] layout =
        [
            (1, 1, 2), (2, 1, 4), (3, 1, 4), (4, 1, 4), (5, 1, 2), (6, 1, 6),
            (1, 2, 4), (2, 2, 4), (3, 2, 4), (4, 2, 4), (5, 2, 4),
            (1, 3, 4), (2, 3, 4), (3, 3, 4), (4, 3, 4), (5, 3, 6)
        ];

        db.Tables.AddRange(layout.Select((table, index) => new Table
        {
            Id = $"T{index + 1}",
            Capacity = table.Capacity,
            CoordinateX = table.Column,
            CoordinateY = table.Row
        }));
    }

    private static async Task AddAdminIfMissingAsync(ApplicationDbContext db, IPasswordHasher<Admin> hasher,
        string email, string password, AdminRole role)
    {
        if (await db.Admins.AnyAsync(a => a.Email == email))
        {
            return;
        }

        var admin = new Admin { Id = Guid.NewGuid().ToString("N"), Email = email, Role = role };
        admin.Password = hasher.HashPassword(admin, password);
        db.Admins.Add(admin);
    }
}
