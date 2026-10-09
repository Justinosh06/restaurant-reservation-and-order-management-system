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

        await db.SaveChangesAsync();
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
