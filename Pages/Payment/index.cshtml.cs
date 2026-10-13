using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Models;
using Stripe;
using Stripe.Checkout;

namespace RestaurantReservation.Pages.Payment;

public class IndexModel : PageModel
{
    public string PaymentId { get; set; } = string.Empty;
    public TransactionStatus Status { get; private set; }
    public string StatusLabel { get; private set; } = string.Empty;
    public string StatusMessage { get; private set; } = string.Empty;
    public string StatusIcon { get; private set; } = string.Empty;
    public string StatusIconClass { get; private set; } = string.Empty;
    public string CurrencySymbol { get; private set; } = string.Empty;
    public string AmountWhole { get; private set; } = string.Empty;
    public string AmountFraction { get; private set; } = string.Empty;
    public string PaymentMethodLabel { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string CreatedAtDisplay { get; private set; } = string.Empty;
    public string ReceiptUrl { get; private set; } = string.Empty;
    public bool ShowReceiptButton => Status is TransactionStatus.Succeeded or TransactionStatus.Refunded && !string.IsNullOrWhiteSpace(ReceiptUrl);
    public bool ShowRetryButton => Status == TransactionStatus.Failed;
    public bool ShowRefreshButton => Status is TransactionStatus.Pending or TransactionStatus.Processing;

    public async Task<IActionResult> OnGet(string paymentId, string? scenario = null)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return NotFound();
        }

        PaymentId = paymentId;

        // Mock preview routes: /Payment/demo-pending, /Payment/demo-processing,
        // /Payment/demo-succeeded, /Payment/demo-failed, /Payment/demo-refunded.
        if (!Enum.TryParse(scenario, true, out TransactionStatus status)
            && !TryGetStatusFromPaymentId(paymentId, out status))
        {
            status = TransactionStatus.Succeeded;
        }

        Status = status;
        StatusLabel = status switch
        {
            TransactionStatus.Pending => "Payment Pending",
            TransactionStatus.Processing => "Payment Processing",
            TransactionStatus.Succeeded => "Payment Successful",
            TransactionStatus.Failed => "Payment Failed",
            TransactionStatus.Refunded => "Payment Refunded",
            _ => "Payment Status Unknown"
        };
        StatusMessage = status switch
        {
            TransactionStatus.Pending => "Your payment is awaiting confirmation.",
            TransactionStatus.Processing => "Your payment is being processed.",
            TransactionStatus.Succeeded => "Your payment was completed successfully.",
            TransactionStatus.Failed => "Your payment could not be completed. Please try again.",
            TransactionStatus.Refunded => "This payment has been refunded.",
            _ => "The latest payment status is unavailable."
        };
        StatusIcon = status switch
        {
            TransactionStatus.Pending => "fa-regular fa-clock",
            TransactionStatus.Processing => "fa-solid fa-spinner",
            TransactionStatus.Succeeded => "fa-solid fa-circle-check",
            TransactionStatus.Failed => "fa-solid fa-circle-xmark",
            TransactionStatus.Refunded => "fa-solid fa-arrow-rotate-left",
            _ => "fa-solid fa-circle-question"
        };
        StatusIconClass = status switch
        {
            TransactionStatus.Pending => "pending",
            TransactionStatus.Processing => "pending",
            TransactionStatus.Succeeded => "success",
            TransactionStatus.Failed => "failed",
            TransactionStatus.Refunded => "refund",
            _ => "failed"
        };

        // These values stand in for the latest stored transaction while the UI is being built.
        var (amount, currency, method, createdAt) = status switch
        {
            TransactionStatus.Pending => (125.50m, "RM", "FPX Online Banking", new DateTime(2026, 10, 10, 15, 40, 0)),
            TransactionStatus.Processing => (89.90m, "RM", "Visa ending in 4242", new DateTime(2026, 10, 10, 15, 38, 0)),
            TransactionStatus.Succeeded => (23000m, "RM", "Touch 'n Go EWallet", new DateTime(2026, 10, 10, 15, 33, 0)),
            TransactionStatus.Failed => (68.00m, "RM", "Mastercard ending in 4444", new DateTime(2026, 10, 10, 15, 30, 0)),
            TransactionStatus.Refunded => (45.00m, "RM", "GrabPay", new DateTime(2026, 10, 9, 12, 15, 0)),
            _ => (0m, "RM", "Unknown", DateTime.UtcNow)
        };

        CurrencySymbol = currency;
        AmountWhole = decimal.Truncate(amount).ToString("N0");
        AmountFraction = ((amount - decimal.Truncate(amount)) * 100).ToString("00");
        PaymentMethodLabel = method;
        CustomerName = "Justin Tan";
        CreatedAtDisplay = createdAt.ToString("dd MMM, yyyy HH:mm");

        ReceiptUrl = await TryGetStripeReceiptUrlAsync(paymentId);

        return Page();
    }

    private static async Task<string> TryGetStripeReceiptUrlAsync(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId) || string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
        {
            return string.Empty;
        }

        try
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(paymentId);
            var paymentIntentId = session.PaymentIntentId;

            if (string.IsNullOrWhiteSpace(paymentIntentId))
            {
                return string.Empty;
            }

            var paymentIntentService = new PaymentIntentService();
            var paymentIntent = await paymentIntentService.GetAsync(paymentIntentId);
            var latestChargeId = paymentIntent.LatestChargeId;

            if (string.IsNullOrWhiteSpace(latestChargeId))
            {
                return string.Empty;
            }

            var chargeService = new ChargeService();
            var charge = await chargeService.GetAsync(latestChargeId);
            return string.IsNullOrWhiteSpace(charge.ReceiptUrl) ? string.Empty : charge.ReceiptUrl;
        }
        catch (StripeException)
        {
            return string.Empty;
        }
    }

    private static bool TryGetStatusFromPaymentId(string paymentId, out TransactionStatus status)
    {
        var statusName = paymentId.Split('-', '_').Last();
        return Enum.TryParse(statusName, true, out status);
    }
}