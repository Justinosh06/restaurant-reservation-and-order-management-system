using RestaurantReservation.Extensions;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Infrastructure.Payments;

// A booking's online deposit as a payment card (Pages/Shared/_PaymentDetails). The customer's Reservations page
// and staff's Reservation Management show the same card, so both build it here.
public static class DepositPayment
{
    // Bookings that went through Stripe Checkout (staff bookings have no deposit).
    public static bool HasPayment(Reservation reservation) =>
        reservation.StripeSessionId != null
        && reservation.DepositStatus is DepositStatus.Paid or DepositStatus.Refunded or DepositStatus.Unpaid;

    // Stripe's details are read only for a deposit that was paid; until then the database is all there is.
    public static Task<StripePaymentInfo?> ReadStripeAsync(Reservation reservation, StripePaymentDetails stripe) =>
        reservation.DepositStatus == DepositStatus.Unpaid
            ? Task.FromResult<StripePaymentInfo?>(null)
            : stripe.GetAsync(reservation.StripeSessionId, expectRefunded: reservation.DepositStatus == DepositStatus.Refunded);

    // forStaff words the refund note for staff ("the customer's card") instead of the customer ("your card").
    public static PaymentPanel Panel(Reservation reservation, StripePaymentInfo? stripe, bool forStaff = false) => new()
    {
        AmountCents = reservation.DepositAmount ?? 0,
        // The currency it was paid in, not today's setting.
        CurrencySymbol = (reservation.DepositCurrency ?? CurrencyType.MYR).GetSymbol(),
        State = reservation.DepositStatus switch
        {
            DepositStatus.Refunded => PaymentState.Refunded,
            DepositStatus.Paid => PaymentState.Paid,
            _ => PaymentState.Pending
        },
        CheckoutSessionId = reservation.StripeSessionId,
        Stripe = stripe,
        FallbackMethod = "Stripe Checkout",
        FallbackName = reservation.Customer?.FullName,
        FallbackEmail = reservation.Customer?.Email,
        FallbackPaidAtUtc = reservation.CreatedAt,
        ForStaff = forStaff
    };
}
