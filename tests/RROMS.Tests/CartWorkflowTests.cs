using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Reservations;
using RestaurantReservation.Models;
using RestaurantReservation.Pages.Table;
using RestaurantReservation.Pages.Table.MenuItem;
using RestaurantReservation.Services;

namespace RROMS.Tests;

public class CartWorkflowTests
{
    [Fact]
    public async Task AddToCart_stores_the_selected_option_and_accepts_300_character_remarks()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        var page = CreateMenuItemPage(database);
        page.Options = new Dictionary<string, string> { ["Pasta"] = "Spaghetti" };
        page.Quantity = 2;
        page.Remarks = new string('x', 300);
        TestDatabase.AttachPageContext(page);

        var result = await page.OnPostAddToCartAsync("T1", "ITEM-1");

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Table/Cart");
        var cartItem = database.Context.CartItems.Single();
        cartItem.Quantity.Should().Be(2);
        cartItem.Remarks.Should().HaveLength(300);
        cartItem.Options.Should().ContainKey("Pasta").WhoseValue.Should().Be("Spaghetti");
    }

    [Fact]
    public async Task AddToCart_rejects_remarks_longer_than_300_characters()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        var page = CreateMenuItemPage(database);
        page.Options = new Dictionary<string, string> { ["Pasta"] = "Spaghetti" };
        page.Remarks = new string('x', 301);
        TestDatabase.AttachPageContext(page);

        var result = await page.OnPostAddToCartAsync("T1", "ITEM-1");

        result.Should().BeOfType<PageResult>();
        page.ModelState.IsValid.Should().BeFalse();
        database.Context.CartItems.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaceOrder_copies_menu_price_and_item_details_into_historical_order_item()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        database.Context.CartItems.Add(new CartItem
        {
            Id = "cart-1",
            SessionId = "session-1",
            MenuItemId = "ITEM-1",
            Quantity = 2,
            Remarks = "No extra salt",
            Options = new Dictionary<string, string> { ["Pasta"] = "Penne" }
        });
        await database.Context.SaveChangesAsync();

        var page = CreateCartPage(database);
        TestDatabase.AttachPageContext(page);
        var result = await page.OnPostPlaceOrderAsync("T1");

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/Table/Checkout");
        var order = database.Context.Orders
            .Include(savedOrder => savedOrder.OrderItems)
            .Single();
        order.Status.Should().Be(OrderStatus.InQueue);
        order.TotalAmount.Should().Be(4980);
        var item = order.OrderItems.Single();
        item.ItemName.Should().Be("Spaghetti Bolognaise");
        item.UnitPrice.Should().Be(2490);
        item.Quantity.Should().Be(2);
        item.Remarks.Should().Be("No extra salt");
        item.Options.Should().ContainKey("Pasta").WhoseValue.Should().Be("Penne");
        database.Context.CartItems.Should().BeEmpty();

        var catalogItem = await database.Context.MenuItems.FindAsync("ITEM-1");
        catalogItem!.Price = 3990;
        await database.Context.SaveChangesAsync();
        (await database.Context.OrderItems.SingleAsync()).UnitPrice.Should().Be(2490);
    }

    private static MenuItemModel CreateMenuItemPage(TestDatabase database)
    {
        var clock = database.CreateClock();
        return new MenuItemModel(
            database.Context,
            new DiningSessionService(database.Context),
            new RestaurantCurrency(database.Context),
            new RestaurantOrderingHours(database.Context, clock));
    }

    private static CartModel CreateCartPage(TestDatabase database)
    {
        var clock = database.CreateClock();
        return new CartModel(
            database.Context,
            new DiningSessionService(database.Context),
            new RestaurantCurrency(database.Context),
            new RestaurantOrderingHours(database.Context, clock));
    }
}
