using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;

namespace RROMS.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(SqliteConnection connection, ApplicationDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    public ApplicationDbContext Context { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(connection, context);
    }

    public async Task SeedTableSessionAndMenuAsync()
    {
        Context.RestaurantSettings.Add(new RestaurantSettings
        {
            Id = "settings",
            Name = "Test Restaurant",
            Address = "1 Test Street",
            DefaultCurrency = CurrencyType.MYR,
            BusinessHours = Enum.GetValues<DayOfWeek>()
                .Select(day => new BusinessHours
                {
                    Id = $"hours-{day}",
                    DayOfWeek = day,
                    OpenTime = TimeOnly.MinValue,
                    CloseTime = TimeOnly.MaxValue,
                    RestaurantSettingsId = "settings"
                })
                .ToList()
        });
        Context.Tables.Add(new Table { Id = "T1", Capacity = 4 });
        Context.Sessions.Add(new Session
        {
            Id = "session-1",
            TableId = "T1",
            StartingTime = DateTime.UtcNow
        });
        Context.MenuItems.Add(new MenuItem
        {
            Id = "ITEM-1",
            Name = "Spaghetti Bolognaise",
            MenuItemType = MenuItemType.Pasta,
            Price = 2490,
            Options = new Dictionary<string, string> { ["Pasta"] = "Spaghetti, Penne" }
        });
        await Context.SaveChangesAsync();
    }

    public RestaurantClock CreateClock() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Restaurant:TimeZone"] = "UTC" })
            .Build());

    public static void AttachPageContext(PageModel page, ClaimsPrincipal? user = null)
    {
        var modelState = new ModelStateDictionary();
        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        page.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new CompiledPageActionDescriptor(), modelState))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
