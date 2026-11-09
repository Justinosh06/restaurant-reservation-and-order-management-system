using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantReservation.Models;
using RestaurantReservation.Pages.Payment;
using RestaurantReservation.Services;

namespace RROMS.Tests;

public class StripeReturnTests
{
    [Fact]
    public async Task Successful_return_records_payment_completes_order_and_binds_guest_customer()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        database.Context.Orders.Add(new Order
        {
            Id = "order-1",
            SessionId = "session-1",
            TotalAmount = 1250,
            Status = OrderStatus.Served
        });
        await database.Context.SaveChangesAsync();

        var checkoutSession = new Stripe.Checkout.Session
        {
            Id = "cs-test",
            AmountTotal = 1250,
            Currency = "myr",
            PaymentStatus = "paid",
            Status = "complete",
            CustomerEmail = "  CUSTOMER@example.com ",
            PaymentMethodTypes = ["card"],
            Metadata = new Dictionary<string, string>
            {
                ["payment_attempt_id"] = "order-1",
                ["table_id"] = "T1",
                ["dining_session_id"] = "session-1",
                ["type"] = "table_settlement"
            }
        };
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.GetCheckoutSessionAsync("cs-test"))
            .ReturnsAsync(checkoutSession);
        var page = new IndexModel(
            database.Context,
            paymentService.Object,
            new DiningSessionService(database.Context),
            NullLogger<IndexModel>.Instance);
        TestDatabase.AttachPageContext(page);

        var result = await page.OnGetAsync("cs-test");

        result.Should().BeOfType<PageResult>();
        page.Status.Should().Be(TransactionStatus.Succeeded);
        page.OrderRecords.Should().ContainSingle()
            .Which.Status.Should().Be(TransactionStatus.Succeeded.ToString());
        var order = await database.Context.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Completed);
        order.CustomerId.Should().NotBeNull();
        var session = await database.Context.Sessions.SingleAsync();
        session.CustomerId.Should().Be(order.CustomerId);
        session.EndingTime.Should().NotBeNull();
        (await database.Context.Customers.SingleAsync()).Email.Should().Be("customer@example.com");
        var transaction = await database.Context.Transactions.SingleAsync();
        transaction.Amount.Should().Be(1250);
        transaction.Status.Should().Be(TransactionStatus.Succeeded);
        transaction.StripeCheckoutSessionId.Should().Be("cs-test");
    }
}
