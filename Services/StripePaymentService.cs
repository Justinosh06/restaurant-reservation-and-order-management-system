using System.Security.Cryptography;
using System.Text;
using Stripe;
using Stripe.Checkout;

namespace RestaurantReservation.Services;

public class StripePaymentService : IPaymentService
{
    private readonly SessionService _sessionService;

    public StripePaymentService()
    {
        _sessionService = new SessionService();
    }

    public async Task<Session> CreateTableSettlementSessionAsync(
        string tableId, 
        string diningSessionId,
        string paymentAttemptId,
        IEnumerable<(string Name, decimal UnitPrice, int Quantity, string Remark)> items,
        int reservationDepositCredit,
        string domain)
    {
        var lineItems = items.Select(item => new SessionLineItemOptions
        {
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "myr",
                UnitAmount = (long)(item.UnitPrice * 100m),
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = item.Name,
                    Description = !string.IsNullOrEmpty(item.Remark) ? $"Remark: {item.Remark}" : null
                }
            },
            Quantity = item.Quantity
        }).ToList();

        var options = new SessionCreateOptions
        {
            LineItems = lineItems,
            Mode = "payment",
            ClientReferenceId = $"table:{tableId}:session:{diningSessionId}:{paymentAttemptId}",
            Metadata = new Dictionary<string, string>
            {
                { "type", "table_settlement" },
                { "table_id", tableId },
                { "dining_session_id", diningSessionId },
                { "payment_attempt_id", paymentAttemptId },
                { "reservation_deposit_credit", reservationDepositCredit.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) }
            },
            SuccessUrl = $"{domain}/Payment/{{CHECKOUT_SESSION_ID}}?tableId={tableId}",
            CancelUrl = $"{domain}/Table/{tableId}/Checkout"
        };

        if (reservationDepositCredit > 0)
        {
            var coupon = await new CouponService().CreateAsync(
                new CouponCreateOptions
                {
                    AmountOff = reservationDepositCredit,
                    Currency = "myr",
                    Duration = "once",
                    Name = "Reservation deposit"
                },
                new RequestOptions
                {
                    IdempotencyKey = CreateIdempotencyKey(
                        "reservation_deposit_credit",
                        $"{tableId}:{diningSessionId}:{paymentAttemptId}")
                });
            options.Discounts =
            [
                new SessionDiscountOptions { Coupon = coupon.Id }
            ];
        }

        return await _sessionService.CreateAsync(options, new RequestOptions
        {
            IdempotencyKey = CreateIdempotencyKey(
                "table_settlement",
                $"{tableId}:{diningSessionId}:{paymentAttemptId}")
        });
    }

    public async Task<Session> CreateReservationDepositSessionAsync(
        string reservationId,
        string customerName,
        string? customerEmail,
        double depositAmount,
        string successUrl,
        string cancelUrl)
    {
        var lineItems = new List<SessionLineItemOptions>
        {
            new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "myr",
                    UnitAmount = (long)(depositAmount * 100),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = $"Table Reservation Deposit - {customerName}",
                        Description = $"Reservation Reference #{reservationId[..8].ToUpperInvariant()}"
                    }
                },
                Quantity = 1
            }
        };

        var options = new SessionCreateOptions
        {
            LineItems = lineItems,
            Mode = "payment",
            CustomerEmail = customerEmail,
            ClientReferenceId = $"reservation:{reservationId}",
            Metadata = new Dictionary<string, string>
            {
                { "type", "reservation_deposit" },
                { "reservation_id", reservationId }
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl
        };

        return await _sessionService.CreateAsync(options, new RequestOptions
        {
            IdempotencyKey = CreateIdempotencyKey("reservation_deposit", reservationId)
        });
    }

    public async Task<Session> GetCheckoutSessionAsync(string sessionId)
    {
        return await _sessionService.GetAsync(sessionId);
    }

    public async Task<Refund> RefundCheckoutSessionAsync(string sessionId)
    {
        var session = await _sessionService.GetAsync(sessionId);
        if (string.IsNullOrEmpty(session.PaymentIntentId))
        {
            throw new InvalidOperationException($"Checkout session {sessionId} has no payment to refund.");
        }

        return await new RefundService().CreateAsync(
            new RefundCreateOptions { PaymentIntent = session.PaymentIntentId },
            new RequestOptions { IdempotencyKey = CreateIdempotencyKey("refund", sessionId) });
    }

    private static string CreateIdempotencyKey(string paymentType, string paymentReference)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentReference)));
        return $"{paymentType}_{hash}";
    }
}