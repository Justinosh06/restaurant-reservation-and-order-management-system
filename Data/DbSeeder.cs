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
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('MenuItems', 'CreatedAt') IS NULL ALTER TABLE MenuItems ADD CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'CheckedInAt') IS NULL ALTER TABLE Reservations ADD CheckedInAt datetime2 NULL");

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

        // ---- Reservation module (multi-table bookings, staff bookings, deposits, check-in) ----

        // Reservations can take several tables, linked through ReservationTables
        // (same table, keys and indexes as EnsureCreated would make for ReservationTable).
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('ReservationTables') IS NULL
            BEGIN
                CREATE TABLE ReservationTables (
                    Id nvarchar(450) NOT NULL,
                    TableId nvarchar(20) NOT NULL,
                    ReservationId nvarchar(450) NOT NULL,
                    CONSTRAINT PK_ReservationTables PRIMARY KEY (Id),
                    CONSTRAINT FK_ReservationTables_Reservations_ReservationId FOREIGN KEY (ReservationId) REFERENCES Reservations (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ReservationTables_Tables_TableId FOREIGN KEY (TableId) REFERENCES Tables (Id)
                );
                CREATE UNIQUE INDEX IX_ReservationTables_ReservationId_TableId ON ReservationTables (ReservationId, TableId);
                CREATE INDEX IX_ReservationTables_TableId ON ReservationTables (TableId);
            END
            """);

        // Bookings staff take by phone or walk-in: guest details instead of a customer account.
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'GuestName') IS NULL ALTER TABLE Reservations ADD GuestName nvarchar(100) NULL");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'ContactNumber') IS NULL ALTER TABLE Reservations ADD ContactNumber nvarchar(30) NULL");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'Notes') IS NULL ALTER TABLE Reservations ADD Notes nvarchar(500) NULL");

        // Those bookings have no customer, so CustomerId becomes optional. Its index and foreign key
        // have to come off while it changes, then go back exactly as EnsureCreated makes them.
        await db.Database.ExecuteSqlRawAsync("""
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Reservations') AND name = 'CustomerId' AND is_nullable = 0)
            BEGIN
                IF OBJECT_ID('FK_Reservations_Customers_CustomerId') IS NOT NULL
                    ALTER TABLE Reservations DROP CONSTRAINT FK_Reservations_Customers_CustomerId;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Reservations') AND name = 'IX_Reservations_CustomerId')
                    DROP INDEX IX_Reservations_CustomerId ON Reservations;
                ALTER TABLE Reservations ALTER COLUMN CustomerId nvarchar(450) NULL;
                CREATE INDEX IX_Reservations_CustomerId ON Reservations (CustomerId);
                ALTER TABLE Reservations ADD CONSTRAINT FK_Reservations_Customers_CustomerId
                    FOREIGN KEY (CustomerId) REFERENCES Customers (Id);
            END
            """);

        // Online bookings pay a deposit through Stripe; existing bookings had none.
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'DepositAmount') IS NULL ALTER TABLE Reservations ADD DepositAmount int NULL");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'DepositStatus') IS NULL ALTER TABLE Reservations ADD DepositStatus nvarchar(20) NOT NULL DEFAULT 'None'");
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('Reservations', 'StripeSessionId') IS NULL ALTER TABLE Reservations ADD StripeSessionId nvarchar(255) NULL");

        // Older databases kept one table per booking in Reservations.TableId (required there).
        // Each booking keeps its table as a ReservationTables row; the old column then becomes
        // optional and empty, so new bookings can be saved and its foreign key can't block removing
        // a table. Dynamic SQL because new databases have no TableId column at all.
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('Reservations', 'TableId') IS NOT NULL
            BEGIN
                EXEC(N'
                    INSERT INTO ReservationTables (Id, ReservationId, TableId)
                    SELECT LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), ''-'', '''')), r.Id, r.TableId
                    FROM Reservations r
                    WHERE r.TableId IS NOT NULL AND r.TableId <> N''''
                      AND EXISTS (SELECT 1 FROM Tables t WHERE t.Id = r.TableId)
                      AND NOT EXISTS (SELECT 1 FROM ReservationTables rt WHERE rt.ReservationId = r.Id AND rt.TableId = r.TableId);');

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Reservations') AND name = 'TableId' AND is_nullable = 0)
                BEGIN
                    IF OBJECT_ID('FK_Reservations_Tables_TableId') IS NOT NULL
                        ALTER TABLE Reservations DROP CONSTRAINT FK_Reservations_Tables_TableId;
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Reservations') AND name = 'IX_Reservations_TableId')
                        DROP INDEX IX_Reservations_TableId ON Reservations;
                    DECLARE @type nvarchar(40) = (SELECT CASE WHEN max_length = -1 THEN N'nvarchar(max)'
                        ELSE N'nvarchar(' + CAST(max_length / 2 AS nvarchar(10)) + N')' END
                        FROM sys.columns WHERE object_id = OBJECT_ID('Reservations') AND name = 'TableId');
                    EXEC(N'ALTER TABLE Reservations ALTER COLUMN TableId ' + @type + N' NULL;');
                END

                EXEC(N'UPDATE Reservations SET TableId = NULL WHERE TableId IS NOT NULL;');
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
