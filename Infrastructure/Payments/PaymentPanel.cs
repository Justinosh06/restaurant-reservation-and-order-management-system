namespace RestaurantReservation.Infrastructure.Payments;

public enum PaymentState
{
    Pending,
    Paid,
    Refunded
}

// One payment card (Pages/Shared/_PaymentDetails): the amount, its state and the Stripe details. The page fills in
// what the database knows; Stripe's details are used where available and the database's otherwise.
public sealed class PaymentPanel
{
    public int AmountCents { get; init; }
    public string CurrencySymbol { get; init; } = "RM";
    public PaymentState State { get; init; }
    public string? CheckoutSessionId { get; init; }
    public StripePaymentInfo? Stripe { get; init; }

    // Used when Stripe's details are not available.
    public string? FallbackMethod { get; init; }
    public string? FallbackName { get; init; }
    public string? FallbackEmail { get; init; }
    public DateTime? FallbackPaidAtUtc { get; init; }

    // Extra lines under the details, e.g. a promo code or the deposit taken off the bill.
    public IReadOnlyList<(string Icon, string Label, string Value)> ExtraRows { get; init; } = [];

    public string? PaymentIntentId => Stripe?.PaymentIntentId;
    public string MethodLabel => Stripe?.MethodLabel ?? FallbackMethod ?? "Not available";
    public string CustomerName => Stripe?.CustomerName ?? FallbackName ?? "Guest";
    public string? CustomerEmail => Stripe?.CustomerEmail ?? FallbackEmail;
    public DateTime? PaidAtUtc => Stripe?.PaidAt ?? FallbackPaidAtUtc;
    public DateTime? RefundedAtUtc => Stripe?.RefundedAt;
    public string? ReceiptUrl => State == PaymentState.Pending ? null : Stripe?.ReceiptUrl;

    public decimal Amount => AmountCents / 100m;
}
