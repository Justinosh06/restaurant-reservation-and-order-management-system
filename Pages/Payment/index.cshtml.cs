using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Services;
using Stripe;
using Stripe.Checkout;

namespace RestaurantReservation.Pages.Payment;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentService _paymentService;

    public IndexModel(ApplicationDbContext db, IPaymentService paymentService)
    {
        _db = db;
        _paymentService = paymentService;
    }

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
    public string BackUrl { get; private set; } = "/";
    public string BackLabel { get; private set; } = "Back to Home";
    public string? RetryUrl { get; private set; }

    public async Task<IActionResult> OnGet(string paymentId, string? scenario = null)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return NotFound();
        }

        PaymentId = paymentId;

        // A table reservation deposit paid on Stripe Checkout (the payment ID is the checkout session ID).
        var deposit = await _db.Reservations.IgnoreQueryFilters()
            .Include(reservation => reservation.Customer)
            .FirstOrDefaultAsync(reservation => reservation.StripeSessionId == paymentId);
        if (deposit is not null)
        {
            if (deposit.CustomerId != User.FindFirstValue(ClaimTypes.NameIdentifier))
            {
                return NotFound();
            }

            await LoadReservationDepositAsync(deposit);
            return Page();
        }

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

    private async Task LoadReservationDepositAsync(Reservation reservation)
    {
        Stripe.Checkout.Session? session = null;
        try
        {
            session = await _paymentService.GetCheckoutSessionAsync(reservation.StripeSessionId!);
        }
        catch (StripeException)
        {
            // Show what the database knows; the customer can refresh once Stripe is reachable.
        }

        // The customer is back from Stripe, so record the payment if it went through.
        if (reservation.DepositStatus == DepositStatus.Unpaid && session?.PaymentStatus == "paid")
        {
            reservation.DepositStatus = DepositStatus.Paid;
            await _db.SaveChangesAsync();
        }

        Status = reservation.DepositStatus switch
        {
            DepositStatus.Paid => TransactionStatus.Succeeded,
            DepositStatus.Refunded => TransactionStatus.Refunded,
            _ when session?.Status == "expired" => TransactionStatus.Failed,
            _ => TransactionStatus.Pending
        };

        var bookedFor = reservation.Date.ToDateTime(reservation.TimeSlot)
            .ToString("ddd d MMM 'at' h:mm tt", CultureInfo.CurrentCulture);
        StatusLabel = Status switch
        {
            TransactionStatus.Succeeded => "Deposit Paid",
            TransactionStatus.Refunded => "Deposit Refunded",
            TransactionStatus.Failed => "Payment Expired",
            _ => "Payment Pending"
        };
        StatusMessage = Status switch
        {
            TransactionStatus.Succeeded => $"Your table for {reservation.Pax} on {bookedFor} is booked. We'll confirm your table soon.",
            TransactionStatus.Refunded => $"Your booking on {bookedFor} was cancelled and the deposit refunded.",
            TransactionStatus.Failed => "The payment window closed before payment, so no booking was made.",
            _ => "We're waiting for Stripe to confirm your payment."
        };
        StatusIcon = Status switch
        {
            TransactionStatus.Succeeded => "fa-solid fa-circle-check",
            TransactionStatus.Refunded => "fa-solid fa-arrow-rotate-left",
            TransactionStatus.Failed => "fa-solid fa-circle-xmark",
            _ => "fa-regular fa-clock"
        };
        StatusIconClass = Status switch
        {
            TransactionStatus.Succeeded => "success",
            TransactionStatus.Refunded => "refund",
            TransactionStatus.Failed => "failed",
            _ => "pending"
        };

        var amount = (reservation.DepositAmount ?? 0) / 100m;
        CurrencySymbol = "RM";
        AmountWhole = decimal.Truncate(amount).ToString("N0");
        AmountFraction = ((amount - decimal.Truncate(amount)) * 100).ToString("00");
        PaymentMethodLabel = await TryGetPaymentMethodLabelAsync(session?.PaymentIntentId) ?? "Stripe Checkout";
        CustomerName = reservation.Customer?.FullName ?? reservation.GuestName ?? "Guest";
        CreatedAtDisplay = reservation.CreatedAt.ToLocalTime().ToString("dd MMM, yyyy HH:mm");
        ReceiptUrl = Status is TransactionStatus.Succeeded or TransactionStatus.Refunded
            ? await TryGetStripeReceiptUrlAsync(reservation.StripeSessionId!)
            : string.Empty;
        BackUrl = "/Customer/Reserve";
        BackLabel = "Back to My Bookings";
        RetryUrl = "/Customer/Reserve";
    }

    // "Visa ending in 4242", or null when Stripe has no card details for the payment.
    private static async Task<string?> TryGetPaymentMethodLabelAsync(string? paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        try
        {
            var paymentIntent = await new PaymentIntentService().GetAsync(paymentIntentId,
                new PaymentIntentGetOptions { Expand = ["payment_method"] });
            var card = paymentIntent.PaymentMethod?.Card;
            return card is null
                ? paymentIntent.PaymentMethod?.Type
                : $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(card.Brand)} ending in {card.Last4}";
        }
        catch (StripeException)
        {
            return null;
        }
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