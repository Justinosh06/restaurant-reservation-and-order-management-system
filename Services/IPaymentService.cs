using Stripe;
using Stripe.Checkout;

namespace RestaurantReservation.Services;

public interface IPaymentService
{
    Task<Session> CreateTableSettlementSessionAsync(
        string tableId, 
        string diningSessionId,
        string paymentAttemptId,
        IEnumerable<(string Name, decimal UnitPrice, int Quantity, string Remark)> items,
        string domain);

    // The checkout page expires after 30 minutes (Stripe's minimum), so an unpaid booking never holds seats for long.
    Task<Session> CreateReservationDepositSessionAsync(
        string reservationId,
        string customerName,
        string? customerEmail,
        double depositAmount,
        string successUrl,
        string cancelUrl);

    Task<Session> GetCheckoutSessionAsync(string sessionId);

    // Refunds everything paid through a checkout session in full.
    Task<Refund> RefundCheckoutSessionAsync(string sessionId);
}