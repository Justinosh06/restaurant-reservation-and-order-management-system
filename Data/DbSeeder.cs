using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Models;

namespace RestaurantReservation.Data;

public static class DbSeeder
{
    // Creates the database (if needed) and the default Administrator and Staff accounts. Nothing else is seeded:
    // customers register themselves, and the menu, reservations and orders are entered into the database.
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
            await AddMissingColumnsAsync(db);
        }

        await AddAdminIfMissingAsync(db, hasher, "admin@restaurant.com", "Admin@123", AdminRole.Administrator);
        await AddAdminIfMissingAsync(db, hasher, "staff@restaurant.com", "Staff@123", AdminRole.Staff);

        await db.SaveChangesAsync();
    }

    // EnsureCreated never changes a database that already exists, so columns added to the models
    // after the database was first created are added here. Each line does nothing once its column exists.
    private static async Task AddMissingColumnsAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Customers', 'Gender') IS NULL ALTER TABLE Customers ADD Gender nvarchar(20) NULL");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Customers', 'DateOfBirth') IS NULL ALTER TABLE Customers ADD DateOfBirth date NULL");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Feedbacks', 'OrderId') IS NULL ALTER TABLE Feedbacks ADD OrderId nvarchar(max) NULL");

        // Options is stored as JSON; existing rows get an empty set of options.
        // EF formats raw SQL like string.Format, so the braces of {} are written doubled.
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('MenuItems', 'Options') IS NULL ALTER TABLE MenuItems ADD Options nvarchar(max) NOT NULL DEFAULT '{{}}'");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('OrderItems', 'Options') IS NULL ALTER TABLE OrderItems ADD Options nvarchar(max) NOT NULL DEFAULT '{{}}'");

        // Same table, keys and indexes as EnsureCreated would make for CartItem.
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('CartItems') IS NULL
            BEGIN
                CREATE TABLE CartItems (
                    Id nvarchar(450) NOT NULL,
                    Quantity int NOT NULL,
                    Options nvarchar(max) NOT NULL,
                    SessionId nvarchar(450) NOT NULL,
                    MenuItemId nvarchar(450) NOT NULL,
                    CONSTRAINT PK_CartItems PRIMARY KEY (Id),
                    CONSTRAINT FK_CartItems_Sessions_SessionId FOREIGN KEY (SessionId) REFERENCES Sessions (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_CartItems_MenuItems_MenuItemId FOREIGN KEY (MenuItemId) REFERENCES MenuItems (Id)
                );
                CREATE INDEX IX_CartItems_SessionId ON CartItems (SessionId);
                CREATE INDEX IX_CartItems_MenuItemId ON CartItems (MenuItemId);
            END
            """);
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
