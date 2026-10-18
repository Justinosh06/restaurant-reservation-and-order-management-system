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
        string paymentAttemptId,
        IEnumerable<(string Name, double UnitPrice, int Quantity, string Remark)> items, 
        string domain)
    {
        var lineItems = items.Select(item => new SessionLineItemOptions
        {
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "myr",
                UnitAmount = (long)(item.UnitPrice * 100),
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
            ClientReferenceId = $"table:{tableId}:{paymentAttemptId}",
            Metadata = new Dictionary<string, string>
            {
                { "type", "table_settlement" },
                { "table_id", tableId },
                { "payment_attempt_id", paymentAttemptId }
            },
            SuccessUrl = $"{domain}/Payment/{{CHECKOUT_SESSION_ID}}?tableId={tableId}",
            CancelUrl = $"{domain}/Table/{tableId}/Checkout"
        };

        return await _sessionService.CreateAsync(options, new RequestOptions
        {
            IdempotencyKey = CreateIdempotencyKey("table_settlement", $"{tableId}:{paymentAttemptId}")
        });
    }

    public async Task<Session> CreateReservationDepositSessionAsync(
        int reservationId, 
        string customerName, 
        double depositAmount, 
        string domain)
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
                        Description = $"Reservation Reference #{reservationId}"
                    }
                },
                Quantity = 1
            }
        };

        var options = new SessionCreateOptions
        {
            LineItems = lineItems,
            Mode = "payment",
            ClientReferenceId = $"reservation:{reservationId}",
            Metadata = new Dictionary<string, string>
            {
                { "type", "reservation_deposit" },
                { "reservation_id", reservationId.ToString() }
            },
            SuccessUrl = $"{domain}/Reservation/Confirmation?sessionId={{CHECKOUT_SESSION_ID}}&reservationId={reservationId}",
            CancelUrl = $"{domain}/Reservation/Cancel?reservationId={reservationId}"
        };

        return await _sessionService.CreateAsync(options, new RequestOptions
        {
            IdempotencyKey = CreateIdempotencyKey("reservation_deposit", reservationId.ToString())
        });
    }

    public async Task<Session> GetCheckoutSessionAsync(string sessionId)
    {
        return await _sessionService.GetAsync(sessionId);
    }

    private static string CreateIdempotencyKey(string paymentType, string paymentReference)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentReference)));
        return $"{paymentType}_{hash}";
    }
}