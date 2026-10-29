using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Payments;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;
using Stripe;

namespace RestaurantReservation.Pages.Payment;

public sealed class PaymentOrderRecord
{
    public string OrderId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
}

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentService _paymentService;
    private readonly DiningSessionService _diningSessions;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        IPaymentService paymentService,
        DiningSessionService diningSessions,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _paymentService = paymentService;
        _diningSessions = diningSessions;
        _logger = logger;
    }

    public string PaymentId { get; private set; } = string.Empty;
    public string PaymentReferenceLabel { get; private set; } = "Stripe Checkout ID";
    public string PaymentIntentId { get; private set; } = string.Empty;
    public string StatusLabel { get; private set; } = string.Empty;
    public string StatusMessage { get; private set; } = string.Empty;
    public string StatusIcon { get; private set; } = string.Empty;
    public string StatusIconClass { get; private set; } = string.Empty;
    public string CurrencySymbol { get; private set; } = string.Empty;
    public string AmountWhole { get; private set; } = string.Empty;
    public string AmountFraction { get; private set; } = string.Empty;
    public string PaymentMethodLabel { get; private set; } = "Not available";
    public string CustomerName { get; private set; } = "Guest";
    public string CustomerEmail { get; private set; } = "Not provided";
    public string CreatedAtDisplay { get; private set; } = string.Empty;
    public string ReceiptUrl { get; private set; } = string.Empty;
    public bool IsCashPayment { get; private set; }
    public string CashTenderedDisplay { get; private set; } = string.Empty;
    public string ChangeDueDisplay { get; private set; } = string.Empty;
    public string TableId { get; private set; } = string.Empty;
    public List<PaymentOrderRecord> OrderRecords { get; private set; } = [];
    public bool HasPaymentError { get; private set; }
    public string? PaymentError { get; private set; }
    public TransactionStatus Status { get; private set; }
    public bool ShowReceiptButton =>
        Status is TransactionStatus.Succeeded or TransactionStatus.Refunded &&
        !string.IsNullOrWhiteSpace(ReceiptUrl);
    public bool ShowRetryButton => Status == TransactionStatus.Failed;
    public bool ShowRefreshButton => Status is TransactionStatus.Pending or TransactionStatus.Processing;

    // Set for a reservation deposit, whose buttons lead back to the customer's bookings instead of a table.
    public string? BackUrl { get; private set; }
    public string BackLabel { get; private set; } = "Back to Table";
    public string? RetryUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return NotFound();
        }

        PaymentId = paymentId;

        var cashPaymentExists = await _db.Transactions.AsNoTracking()
            .AnyAsync(transaction =>
                transaction.Id == paymentId &&
                transaction.PaymentMethod == RestaurantReservation.Models.PaymentMethod.Cash);
        if (cashPaymentExists)
        {
            var cashPayments = await LoadSucceededCashPaymentsAsync(paymentId);
            if (cashPayments is null)
            {
                return NotFound();
            }

            LoadCashPayment(cashPayments);
            return Page();
        }

        // A table reservation deposit paid on Stripe Checkout (the payment ID is the checkout session ID).
        // Checked first: the table settlement code below only accepts payments made at a table.
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

        try
        {
            var checkoutSession = await _paymentService.GetCheckoutSessionAsync(paymentId);
            PaymentIntentId = checkoutSession.PaymentIntentId ?? string.Empty;
            var paymentIntent = string.IsNullOrWhiteSpace(checkoutSession.PaymentIntentId)
                ? null
                : await new PaymentIntentService().GetAsync(checkoutSession.PaymentIntentId);

            Stripe.Charge? charge = null;
            if (!string.IsNullOrWhiteSpace(paymentIntent?.LatestChargeId))
            {
                charge = await new ChargeService().GetAsync(paymentIntent.LatestChargeId);
                ReceiptUrl = charge.ReceiptUrl ?? string.Empty;
            }

            var orders = await LoadRelatedOrdersAsync(checkoutSession);
            if (orders is null)
            {
                return NotFound();
            }

            TableId = checkoutSession.Metadata.GetValueOrDefault("table_id", string.Empty);
            if (string.IsNullOrWhiteSpace(TableId) ||
                !string.Equals(checkoutSession.Metadata.GetValueOrDefault("type"), "table_settlement", StringComparison.Ordinal))
            {
                return NotFound();
            }

            Status = MapStatus(checkoutSession, paymentIntent?.Status, charge?.Refunded ?? false);
            StatusLabel = Status switch
            {
                TransactionStatus.Pending => "Payment Pending",
                TransactionStatus.Processing => "Payment Processing",
                TransactionStatus.Succeeded => "Payment Successful",
                TransactionStatus.Failed => "Payment Failed",
                TransactionStatus.Refunded => "Payment Refunded",
                _ => "Payment Status Unknown"
            };
            StatusMessage = Status switch
            {
                TransactionStatus.Pending => "Your payment is awaiting confirmation.",
                TransactionStatus.Processing => "Your payment is being processed.",
                TransactionStatus.Succeeded => "Your payment was completed successfully.",
                TransactionStatus.Failed => "Your payment could not be completed. Please try again.",
                TransactionStatus.Refunded => "This payment has been refunded.",
                _ => "The latest payment status is unavailable."
            };
            StatusIcon = Status switch
            {
                TransactionStatus.Pending => "fa-regular fa-clock",
                TransactionStatus.Processing => "fa-solid fa-spinner",
                TransactionStatus.Succeeded => "fa-solid fa-circle-check",
                TransactionStatus.Failed => "fa-solid fa-circle-xmark",
                TransactionStatus.Refunded => "fa-solid fa-arrow-rotate-left",
                _ => "fa-solid fa-circle-question"
            };
            StatusIconClass = Status switch
            {
                TransactionStatus.Pending or TransactionStatus.Processing => "pending",
                TransactionStatus.Succeeded => "success",
                TransactionStatus.Refunded => "refund",
                _ => "failed"
            };

            if (checkoutSession.AmountTotal is null || checkoutSession.AmountTotal < 0)
            {
                throw new InvalidOperationException("Stripe checkout session did not include a valid amount.");
            }

            var amount = checkoutSession.AmountTotal.Value / 100m;
            CurrencySymbol = GetCurrencySymbol(checkoutSession.Currency);
            AmountWhole = decimal.Truncate(amount).ToString("N0");
            AmountFraction = ((amount - decimal.Truncate(amount)) * 100).ToString("00");
            PaymentMethodLabel = GetPaymentMethodLabel(charge, checkoutSession);
            CustomerName = checkoutSession.CustomerDetails?.Name ?? "Guest";
            CustomerEmail = checkoutSession.CustomerDetails?.Email
                ?? checkoutSession.CustomerEmail
                ?? "Not provided";
            var paymentCreatedAt = charge?.Created ?? checkoutSession.Created;
            CreatedAtDisplay = paymentCreatedAt.ToLocalTime().ToString("dd MMM, yyyy HH:mm");

            await SaveTransactionRecordsAsync(
                orders,
                checkoutSession,
                paymentIntent?.Id,
                charge,
                Status,
                checkoutSession.CustomerDetails?.Email ?? checkoutSession.CustomerEmail);

            var orderIds = orders.Select(order => order.Id).ToArray();
            OrderRecords = await _db.Transactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.StripeCheckoutSessionId == checkoutSession.Id &&
                    orderIds.Contains(transaction.OrderId))
                .OrderBy(transaction => transaction.OrderId)
                .Select(transaction => new PaymentOrderRecord
                {
                    OrderId = transaction.OrderId,
                    TransactionId = transaction.Id,
                    Status = transaction.Status.ToString(),
                    Amount = $"{GetCurrencySymbol(checkoutSession.Currency)} {transaction.Amount / 100m:F2}"
                })
                .ToListAsync();

            return Page();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment details could not be retrieved for checkout session {PaymentId}.", paymentId);
            HasPaymentError = true;
            PaymentError = "We could not retrieve the payment details from Stripe. Please try again later.";
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return Page();
        }
    }

    public async Task<IActionResult> OnGetCashReceiptAsync(string paymentId)
    {
        var payments = await LoadSucceededCashPaymentsAsync(paymentId);
        if (payments is null)
        {
            return NotFound();
        }

        var receipt = await BuildCashReceiptAsync(payments);
        return File(
            CashReceiptPdf.Write(receipt),
            "application/pdf",
            $"cash-receipt-{receipt.ReceiptNumber}.pdf");
    }

    private async Task<List<Transaction>?> LoadSucceededCashPaymentsAsync(string paymentId)
    {
        var anchor = await _db.Transactions.AsNoTracking()
            .Where(transaction =>
                transaction.Id == paymentId &&
                transaction.PaymentMethod == RestaurantReservation.Models.PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Succeeded &&
                transaction.StripeCheckoutSessionId == null)
            .Select(transaction => new { transaction.Order!.SessionId, transaction.MetadataJson })
            .SingleOrDefaultAsync();
        if (anchor is null)
        {
            return null;
        }

        var payments = await _db.Transactions.AsNoTracking()
            .Include(transaction => transaction.Order!.OrderItems)
            .Include(transaction => transaction.Order!.Customer)
            .Include(transaction => transaction.Order!.Session!.Customer)
            .Where(transaction =>
                transaction.Order!.SessionId == anchor.SessionId &&
                transaction.PaymentMethod == RestaurantReservation.Models.PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Succeeded &&
                transaction.StripeCheckoutSessionId == null)
            .OrderBy(transaction => transaction.OrderId)
            .ToListAsync();

        var paymentGroupId = ReadMetadataText(anchor.MetadataJson, "cashPaymentGroupId");
        if (!string.IsNullOrWhiteSpace(paymentGroupId))
        {
            payments = payments
                .Where(transaction => ReadMetadataText(transaction.MetadataJson, "cashPaymentGroupId") == paymentGroupId)
                .ToList();
        }

        return payments.Any(transaction => transaction.Id == paymentId) ? payments : null;
    }

    private void LoadCashPayment(IReadOnlyList<Transaction> payments)
    {
        var firstPayment = payments[0];
        var firstOrder = firstPayment.Order!;
        var session = firstOrder.Session!;
        var customer = firstOrder.Customer ?? session.Customer;
        var tenderedCents = ReadMetadataCents(firstPayment.MetadataJson, "cashTendered");
        var changeCents = ReadMetadataCents(firstPayment.MetadataJson, "changeDue");

        IsCashPayment = true;
        PaymentReferenceLabel = "Cash receipt reference";
        Status = TransactionStatus.Succeeded;
        StatusLabel = "Cash Payment Received";
        StatusMessage = $"Your cash payment at the counter was recorded. Change returned: {firstPayment.Currency.FormatCents(changeCents)}.";
        StatusIcon = "fa-solid fa-circle-check";
        StatusIconClass = "success";
        CurrencySymbol = firstPayment.Currency.GetSymbol();
        var amount = payments.Sum(payment => payment.Amount) / 100m;
        AmountWhole = decimal.Truncate(amount).ToString("N0");
        AmountFraction = ((amount - decimal.Truncate(amount)) * 100).ToString("00");
        PaymentMethodLabel = "Cash";
        CustomerName = customer?.FullName is { Length: > 0 } fullName ? fullName : "Walk-in guest";
        CustomerEmail = customer?.Email ?? "Not provided";
        CreatedAtDisplay = payments.Max(payment => payment.CreatedAt).ToLocalTime().ToString("dd MMM, yyyy HH:mm");
        TableId = session.TableId;
        CashTenderedDisplay = firstPayment.Currency.FormatCents(tenderedCents);
        ChangeDueDisplay = firstPayment.Currency.FormatCents(changeCents);
        ReceiptUrl = $"/Payment/{Uri.EscapeDataString(PaymentId)}?handler=CashReceipt";
    }

    private async Task<CashReceiptData> BuildCashReceiptAsync(IReadOnlyList<Transaction> payments)
    {
        var firstPayment = payments[0];
        var diningSession = firstPayment.Order!.Session!;
        var settings = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        var customer = firstPayment.Order.Customer ?? diningSession.Customer;
        var customerName = string.IsNullOrWhiteSpace(customer?.FullName)
            ? "Walk-in guest"
            : customer.FullName;

        return new CashReceiptData(
            settings?.Name ?? "Restaurant",
            settings?.Address ?? string.Empty,
            $"CASH-{(ReadMetadataText(firstPayment.MetadataJson, "cashPaymentGroupId") ?? firstPayment.Id)[..8].ToUpperInvariant()}",
            diningSession.TableId,
            customerName,
            customer?.Email ?? "Not provided",
            firstPayment.Currency,
            payments.Max(payment => payment.CreatedAt),
            ReadMetadataCents(firstPayment.MetadataJson, "cashTendered"),
            ReadMetadataCents(firstPayment.MetadataJson, "changeDue"),
            payments.Select(payment => new CashReceiptOrder(
                payment.Order!.Id,
                payment.Order.CreatedAt,
                payment.Order.TotalAmount,
                ReadMetadataCents(payment.MetadataJson, "promoDiscount"),
                ReadMetadataCents(payment.MetadataJson, "reservationDepositCredit"),
                ReadMetadataText(payment.MetadataJson, "promotionCode"),
                payment.Amount,
                payment.Order.OrderItems
                    .Select(item => new CashReceiptItem(item.ItemName, item.Quantity, item.UnitPrice))
                    .ToList()))
                .ToList());
    }

    private static int ReadMetadataCents(string? metadataJson, string name)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return 0;
        }

        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var cents)
            ? cents
            : 0;
    }

    private static string? ReadMetadataText(string? metadataJson, string name)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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
        CurrencySymbol = (reservation.DepositCurrency ?? CurrencyType.MYR).GetSymbol();
        AmountWhole = decimal.Truncate(amount).ToString("N0");
        AmountFraction = ((amount - decimal.Truncate(amount)) * 100).ToString("00");
        PaymentMethodLabel = await TryGetPaymentMethodLabelAsync(session?.PaymentIntentId) ?? "Stripe Checkout";
        CustomerName = reservation.Customer?.FullName ?? reservation.GuestName ?? "Guest";
        CustomerEmail = reservation.Customer?.Email ?? "Not provided";
        PaymentIntentId = session?.PaymentIntentId ?? string.Empty;
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
            var sessionService = new Stripe.Checkout.SessionService();
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

    private async Task<List<Order>?> LoadRelatedOrdersAsync(Stripe.Checkout.Session checkoutSession)
    {
        if (!checkoutSession.Metadata.TryGetValue("payment_attempt_id", out var orderIdsValue) ||
            string.IsNullOrWhiteSpace(orderIdsValue))
        {
            return null;
        }

        var tableId = checkoutSession.Metadata.GetValueOrDefault("table_id", string.Empty);
        var orderIds = orderIdsValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (orderIds.Length == 0)
        {
            return null;
        }

        var orders = await _db.Orders
            .Include(order => order.Session)
            .Where(order => orderIds.Contains(order.Id) && order.Session!.TableId == tableId)
            .ToListAsync();
        var diningSessionId = checkoutSession.Metadata.GetValueOrDefault("dining_session_id");

        return orders.Count == orderIds.Length &&
            orders.All(order => order.Session is not null) &&
            orders.Select(order => order.SessionId).Distinct(StringComparer.Ordinal).Count() == 1 &&
            (string.IsNullOrWhiteSpace(diningSessionId) ||
             orders.All(order => order.SessionId == diningSessionId))
                ? orders
                : null;
    }

    private async Task SaveTransactionRecordsAsync(
        IReadOnlyCollection<Order> orders,
        Stripe.Checkout.Session checkoutSession,
        string? paymentIntentId,
        Stripe.Charge? charge,
        TransactionStatus status,
        string? customerEmail)
    {
        var currency = ParseCurrency(checkoutSession.Currency);
        var paymentMethod = ParsePaymentMethod(charge, checkoutSession);
        var stripeCreatedAt = charge?.Created ?? checkoutSession.Created;
        var transactionTime = stripeCreatedAt == default
            ? DateTime.UtcNow
            : stripeCreatedAt.ToUniversalTime();
        var distinctOrders = orders
            .GroupBy(order => order.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(order => order.Id, StringComparer.Ordinal)
            .ToList();
        var depositCreditRemaining = status == TransactionStatus.Succeeded &&
                                     int.TryParse(
                                         checkoutSession.Metadata.GetValueOrDefault("reservation_deposit_credit"),
                                         NumberStyles.Integer,
                                         CultureInfo.InvariantCulture,
                                         out var parsedCredit)
            ? parsedCredit
            : 0;
        var promoRemaining = status == TransactionStatus.Succeeded &&
                             int.TryParse(
                                 checkoutSession.Metadata.GetValueOrDefault("promo_discount"),
                                 NumberStyles.Integer,
                                 CultureInfo.InvariantCulture,
                                 out var parsedPromo)
            ? parsedPromo
            : 0;
        var promotionCode = checkoutSession.Metadata.GetValueOrDefault("promotion_code");

        foreach (var order in distinctOrders)
        {
            // The promo discount comes off each order first, then the deposit (as on the checkout page).
            var orderPromoDiscount = Math.Min(order.TotalAmount, promoRemaining);
            var orderDepositCredit = Math.Min(order.TotalAmount - orderPromoDiscount, depositCreditRemaining);
            var transaction = _db.Transactions.Local
                .FirstOrDefault(existing =>
                    existing.OrderId == order.Id &&
                    existing.StripeCheckoutSessionId == checkoutSession.Id)
                ?? await _db.Transactions.FirstOrDefaultAsync(existing =>
                    existing.OrderId == order.Id &&
                    existing.StripeCheckoutSessionId == checkoutSession.Id);

            var metadata = JsonSerializer.Serialize(new
            {
                checkoutSessionId = checkoutSession.Id,
                paymentIntentId,
                stripePaymentStatus = checkoutSession.PaymentStatus,
                stripeSessionStatus = checkoutSession.Status,
                receiptUrl = charge?.ReceiptUrl,
                reservationDepositCredit = orderDepositCredit,
                promoDiscount = orderPromoDiscount,
                promotionCode = orderPromoDiscount > 0 ? promotionCode : null
            });

            if (transaction is null)
            {
                transaction = new Transaction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OrderId = order.Id,
                    StripeCheckoutSessionId = checkoutSession.Id
                };
                _db.Transactions.Add(transaction);
            }

            transaction.Amount = status == TransactionStatus.Succeeded
                ? order.TotalAmount - orderPromoDiscount - orderDepositCredit
                : order.TotalAmount;
            transaction.Currency = currency;
            transaction.Status = status;
            transaction.PaymentMethod = paymentMethod;
            transaction.CreatedAt = transactionTime;
            transaction.MetadataJson = metadata;
            depositCreditRemaining -= orderDepositCredit;
            promoRemaining -= orderPromoDiscount;
        }

        if (status == TransactionStatus.Succeeded)
        {
            var paidOrderIds = distinctOrders.Select(order => order.Id).ToArray();
            await _db.Transactions
                .Where(transaction =>
                    paidOrderIds.Contains(transaction.OrderId) &&
                    transaction.PaymentMethod == RestaurantReservation.Models.PaymentMethod.Cash &&
                    transaction.Status == TransactionStatus.Pending)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(transaction => transaction.Status, TransactionStatus.Failed));

            var session = distinctOrders.First().Session
                ?? throw new InvalidOperationException("A paid order is not associated with a dining session.");

            var customer = session.CustomerId is null
                ? await FindOrCreateCustomerAsync(customerEmail)
                : await _db.Customers.FindAsync(session.CustomerId)
                    ?? throw new InvalidOperationException(
                        $"Dining session {session.Id} references a customer that does not exist.");
            var customerId = session.CustomerId ?? customer?.Id;
            foreach (var order in distinctOrders)
            {
                order.Status = OrderStatus.Completed;
                if (customerId is not null)
                {
                    order.CustomerId = customerId;
                }
            }

            session.CustomerId ??= customerId;
            session.EndingTime ??= DateTime.UtcNow;
            await _diningSessions.CompleteCheckedInReservationsAsync(session);
        }

        await _db.SaveChangesAsync();
    }

    private async Task<RestaurantReservation.Models.Customer?> FindOrCreateCustomerAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var customer = await _db.Customers
            .FirstOrDefaultAsync(account => account.Email.ToLower() == normalizedEmail);
        if (customer is not null)
        {
            return customer;
        }

        customer = new RestaurantReservation.Models.Customer
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = normalizedEmail
        };
        _db.Customers.Add(customer);
        return customer;
    }

    private static TransactionStatus MapStatus(
        Stripe.Checkout.Session checkoutSession,
        string? paymentIntentStatus,
        bool refunded)
    {
        if (refunded)
        {
            return TransactionStatus.Refunded;
        }

        return paymentIntentStatus switch
        {
            "succeeded" => TransactionStatus.Succeeded,
            "processing" => TransactionStatus.Processing,
            "requires_payment_method" or "canceled" => TransactionStatus.Failed,
            "requires_action" or "requires_confirmation" or "requires_capture" => TransactionStatus.Pending,
            _ when checkoutSession.PaymentStatus == "paid" => TransactionStatus.Succeeded,
            _ when checkoutSession.Status == "expired" => TransactionStatus.Failed,
            _ when checkoutSession.Status == "complete" => TransactionStatus.Processing,
            _ => TransactionStatus.Pending
        };
    }

    private static CurrencyType ParseCurrency(string? currency)
    {
        return currency?.ToLowerInvariant() switch
        {
            "myr" => CurrencyType.MYR,
            "usd" => CurrencyType.USD,
            "eur" => CurrencyType.EUR,
            _ => throw new InvalidOperationException($"Unsupported Stripe currency '{currency}'.")
        };
    }

    private static string GetCurrencySymbol(string? currency)
    {
        return currency?.ToLowerInvariant() switch
        {
            "myr" => "RM",
            "usd" => "$",
            "eur" => "€",
            _ => currency?.ToUpperInvariant() ?? string.Empty
        };
    }

    private static RestaurantReservation.Models.PaymentMethod ParsePaymentMethod(
        Stripe.Charge? charge,
        Stripe.Checkout.Session checkoutSession)
    {
        var method = charge?.PaymentMethodDetails?.Type
            ?? checkoutSession.PaymentMethodTypes.FirstOrDefault();

        return method?.ToLowerInvariant() switch
        {
            "card" => RestaurantReservation.Models.PaymentMethod.Card,
            "fpx" => RestaurantReservation.Models.PaymentMethod.Fpx,
            "grabpay" => RestaurantReservation.Models.PaymentMethod.GrabPay,
            "alipay" or "link" => RestaurantReservation.Models.PaymentMethod.EWallet,
            "cash" => RestaurantReservation.Models.PaymentMethod.Cash,
            _ => RestaurantReservation.Models.PaymentMethod.Other
        };
    }

    private static string GetPaymentMethodLabel(
        Stripe.Charge? charge,
        Stripe.Checkout.Session checkoutSession)
    {
        var details = charge?.PaymentMethodDetails;
        if (details?.Card is { } card)
        {
            var brand = card.Brand ?? "Card";
            return string.IsNullOrWhiteSpace(card.Last4)
                ? brand
                : $"{brand} ending in {card.Last4}";
        }

        var method = details?.Type ?? checkoutSession.PaymentMethodTypes.FirstOrDefault();
        return method switch
        {
            "fpx" => "FPX Online Banking",
            "grabpay" => "GrabPay",
            "alipay" => "Alipay",
            "link" => "Link",
            "card" => "Card",
            null or "" => "Not available",
            _ => method
        };
    }
}
