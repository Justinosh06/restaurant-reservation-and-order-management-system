using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantReservation.Infrastructure.Payments;
using RestaurantReservation.Models;
using Restaurant_Reservation_and_Order_Management_System.Pages.Customer.Dining;

namespace RROMS.Tests;

public class CustomerVisitTests
{
    [Fact]
    public async Task Visit_shows_only_the_customers_orders_and_excludes_cancelled_totals()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        database.Context.Customers.Add(new Customer
        {
            Id = "customer-1",
            Email = "customer@example.com"
        });
        database.Context.Sessions.Single().CustomerId = "customer-1";
        database.Context.Orders.AddRange(
            new Order
            {
                Id = "order-served",
                SessionId = "session-1",
                CustomerId = "customer-1",
                TotalAmount = 2400,
                Status = OrderStatus.Served
            },
            new Order
            {
                Id = "order-cancelled",
                SessionId = "session-1",
                CustomerId = "customer-1",
                TotalAmount = 900,
                Status = OrderStatus.Cancelled
            });
        await database.Context.SaveChangesAsync();

        var ownerPage = CreateVisitPage(database);
        TestDatabase.AttachPageContext(ownerPage, User("customer-1"));

        var ownerResult = await ownerPage.OnGetAsync("session-1");

        ownerResult.Should().BeOfType<PageResult>();
        ownerPage.Visit.TotalAmount.Should().Be(2400);
        ownerPage.Visit.Orders.Should().HaveCount(2);

        var otherCustomerPage = CreateVisitPage(database);
        TestDatabase.AttachPageContext(otherCustomerPage, User("customer-2"));
        var otherCustomerResult = await otherCustomerPage.OnGetAsync("session-1");
        otherCustomerResult.Should().BeOfType<NotFoundResult>();
    }

    private static VisitModel CreateVisitPage(TestDatabase database) =>
        new(
            database.Context,
            new StripePaymentDetails(new MemoryCache(new MemoryCacheOptions()), NullLogger<StripePaymentDetails>.Instance));

    private static ClaimsPrincipal User(string customerId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, customerId)], "test"));
}
