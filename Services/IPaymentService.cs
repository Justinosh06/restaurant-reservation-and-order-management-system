using Stripe.Checkout;

namespace RestaurantReservation.Services;

public interface IPaymentService
{
    Task<Session> CreateTableSettlementSessionAsync(
        string tableId, 
        string paymentAttemptId,
        IEnumerable<(string Name, double UnitPrice, int Quantity, string Remark)> items, 
        string domain);

    Task<Session> CreateReservationDepositSessionAsync(
        int reservationId, 
        string customerName, 
        double depositAmount, 
        string domain);

    Task<Session> GetCheckoutSessionAsync(string sessionId);
}