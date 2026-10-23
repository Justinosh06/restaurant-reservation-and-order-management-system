using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Stripe;
using Stripe.Checkout;

namespace RestaurantReservation.Infrastructure.Payments;

// What Stripe knows about one Checkout payment, for the payment details shown to the customer on their dining
// visits and reservations (the same details as the payment result page, Pages/Payment).
public sealed record StripePaymentInfo(
    string CheckoutSessionId,
    string? PaymentIntentId,
    string? MethodLabel,
    string? CustomerName,
    string? CustomerEmail,
    DateTime? PaidAt,
    string? ReceiptUrl,
    bool Refunded,
    DateTime? RefundedAt);

// Reads payment details from Stripe. Results are cached so the customer's pages stay quick; a payment that
// should now be refunded (the booking was just cancelled) is read again until Stripe shows the refund.
public class StripePaymentDetails(IMemoryCache cache, ILogger<StripePaymentDetails> logger)
{
    private static readonly TimeSpan PaidCacheTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefundedCacheTime = TimeSpan.FromHours(12);

    // Null when Stripe is not set up or can't be reached; the page then shows what the database knows.
    public async Task<StripePaymentInfo?> GetAsync(string? checkoutSessionId, bool expectRefunded = false)
    {
        if (string.IsNullOrWhiteSpace(checkoutSessionId) || string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
        {
            return null;
        }

        var key = "stripe-payment:" + checkoutSessionId;
        if (cache.TryGetValue(key, out StripePaymentInfo? cached) && cached is not null && (!expectRefunded || cached.Refunded))
        {
            return cached;
        }

        try
        {
            var session = await new SessionService().GetAsync(checkoutSessionId,
                new SessionGetOptions { Expand = ["payment_intent.latest_charge"] });
            var intent = session.PaymentIntent;
            var charge = intent?.LatestCharge;

            var refunded = charge is not null && (charge.Refunded || charge.AmountRefunded > 0);
            DateTime? refundedAt = null;
            if (refunded && intent is not null)
            {
                var refunds = await new RefundService().ListAsync(new RefundListOptions { PaymentIntent = intent.Id, Limit = 10 });
                refundedAt = refunds.Data.Count == 0 ? null : refunds.Data.Max(refund => refund.Created);
            }

            var info = new StripePaymentInfo(
                session.Id,
                intent?.Id ?? session.PaymentIntentId,
                MethodLabel(charge),
                session.CustomerDetails?.Name,
                session.CustomerDetails?.Email ?? session.CustomerEmail,
                charge?.Created ?? (session.PaymentStatus == "paid" ? session.Created : null),
                charge?.ReceiptUrl,
                refunded,
                refundedAt);

            cache.Set(key, info, refunded ? RefundedCacheTime : PaidCacheTime);
            return info;
        }
        catch (StripeException error)
        {
            logger.LogWarning(error, "Could not read payment details for checkout session {CheckoutSessionId}.", checkoutSessionId);
            return null;
        }
    }

    // "Visa ending in 4242", or the payment type for other methods.
    private static string? MethodLabel(Charge? charge)
    {
        var details = charge?.PaymentMethodDetails;
        if (details?.Card is { } card)
        {
            var brand = string.IsNullOrWhiteSpace(card.Brand)
                ? "Card"
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(card.Brand);
            return string.IsNullOrWhiteSpace(card.Last4) ? brand : $"{brand} ending in {card.Last4}";
        }

        return details?.Type switch
        {
            null or "" => null,
            "fpx" => "FPX Online Banking",
            "grabpay" => "GrabPay",
            "alipay" => "Alipay",
            "link" => "Link",
            var type => type
        };
    }
}
