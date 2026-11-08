using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;
using RestaurantReservation.Pages.Table;
using RestaurantReservation.Services;

namespace RROMS.Tests;

public class CheckoutStatusTests
{
    [Fact]
    public async Task Status_reports_order_actions_and_cash_payment_then_refreshes_after_payment()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedTableSessionAndMenuAsync();
        database.Context.Orders.AddRange(
            new Order
            {
                Id = "order-queued",
                SessionId = "session-1",
                TotalAmount = 1200,
                Status = OrderStatus.InQueue,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new Order
            {
                Id = "order-served",
                SessionId = "session-1",
                TotalAmount = 800,
                Status = OrderStatus.Served,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        database.Context.Transactions.Add(new Transaction
        {
            Id = "cash-payment",
            OrderId = "order-served",
            Amount = 800,
            Currency = RestaurantReservation.Models.Enums.CurrencyType.MYR,
            Status = TransactionStatus.Pending,
            PaymentMethod = PaymentMethod.Cash
        });
        await database.Context.SaveChangesAsync();

        var page = CreateCheckoutPage(database);
        var pendingResult = (JsonResult)await page.OnGetStatusAsync("session-1", ["cash-payment"]);
        var pendingStatus = JsonSerializer.SerializeToElement(pendingResult.Value);

        pendingStatus.GetProperty("canPay").GetBoolean().Should().BeFalse();
        pendingStatus.GetProperty("hasPayableOrders").GetBoolean().Should().BeTrue();
        pendingStatus.GetProperty("cashPaymentPending").GetBoolean().Should().BeTrue();
        pendingStatus.GetProperty("orders")[0].GetProperty("canCancel").GetBoolean().Should().BeTrue();
        pendingStatus.GetProperty("orders")[0].GetProperty("isPayable").GetBoolean().Should().BeTrue();

        var queuedOrder = await database.Context.Orders.SingleAsync(order => order.Id == "order-queued");
        queuedOrder.Status = OrderStatus.Served;
        var cashPayment = await database.Context.Transactions.SingleAsync(transaction => transaction.Id == "cash-payment");
        cashPayment.Status = TransactionStatus.Succeeded;
        await database.Context.SaveChangesAsync();

        var paidResult = (JsonResult)await page.OnGetStatusAsync("session-1", ["cash-payment"]);
        var paidStatus = JsonSerializer.SerializeToElement(paidResult.Value);
        paidStatus.GetProperty("canPay").GetBoolean().Should().BeTrue();
        paidStatus.GetProperty("cashPaid").GetBoolean().Should().BeTrue();
        paidStatus.GetProperty("paymentStatusUrl").GetString().Should().Be("/Payment/cash-payment");
    }

    private static CheckoutModel CreateCheckoutPage(TestDatabase database) =>
        new(
            database.Context,
            Mock.Of<IPaymentService>(),
            new DiningSessionService(database.Context),
            database.CreateClock(),
            new RestaurantCurrency(database.Context),
            NullLogger<CheckoutModel>.Instance);
}
