using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table;

public class CheckoutOrderItem
{
    public string Name { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string? Remark { get; init; }
    public string Options { get; init; } = string.Empty;
}

public class SessionOrderViewModel
{
    public string OrderId { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public string FormattedTime { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public bool CanCancel { get; init; }
    public bool IsPayable { get; init; }
    public bool IsServed { get; init; }
    public List<CheckoutOrderItem> Items { get; init; } = [];
}

public class CheckoutModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentService _paymentService;
    private readonly DiningSessionService _diningSessions;
    private readonly RestaurantClock _clock;
    private readonly RestaurantCurrency _restaurantCurrency;
    private readonly ILogger<CheckoutModel> _logger;

    public CheckoutModel(
        ApplicationDbContext db,
        IPaymentService paymentService,
        DiningSessionService diningSessions,
        RestaurantClock clock,
        RestaurantCurrency restaurantCurrency,
        ILogger<CheckoutModel> logger)
    {
        _db = db;
        _paymentService = paymentService;
        _diningSessions = diningSessions;
        _clock = clock;
        _restaurantCurrency = restaurantCurrency;
        _logger = logger;
    }

    public string TableId { get; private set; } = string.Empty;
    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public string CurrencySymbol => Currency.GetSymbol();
    public bool IsCustomerBound { get; private set; }
    public bool IsSessionClosed { get; private set; }
    public List<SessionOrderViewModel> SessionOrders { get; private set; } = [];
    public decimal TotalAmount => SessionOrders
        .Where(order => order.IsPayable)
        .Sum(order => order.TotalAmount);
    public decimal ReservationDepositCredit { get; private set; }

    // The promotion whose code this table applied, and what it takes off the orders total right now
    // (0 while the bill is under the promotion's minimum spend).
    public Promotion? AppliedPromotion { get; private set; }
    public decimal PromoDiscount { get; private set; }
    public decimal AmountDue => Math.Max(0, TotalAmount - PromoDiscount - ReservationDepositCredit);

    [TempData]
    public string? PromoMessage { get; set; }

    [TempData]
    public string? PromoError { get; set; }

    // Why an applied code was just taken off, when that happens while the page loads.
    public string? PromoRemovedNotice { get; private set; }
    public bool CanPay => SessionOrders.Any(order => order.IsPayable) &&
        SessionOrders.Where(order => order.IsPayable).All(order => order.IsServed);

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string tableId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        Currency = await _restaurantCurrency.GetAsync();
        IsCustomerBound = session.Session!.CustomerId is not null;
        await LoadSessionOrdersAsync(session.Session!.Id);
        PromoRemovedNotice = await LoadPromotionAsync(session.Session);
        await LoadReservationDepositCreditAsync(session.Session);
        ViewData["CartItemCount"] = await _diningSessions.GetCartItemCountAsync(session.Session.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostPayWithStripeAsync(string tableId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        Currency = await _restaurantCurrency.GetAsync();
        IsCustomerBound = session.Session!.CustomerId is not null;
        await LoadSessionOrdersAsync(session.Session!.Id);
        var promoRemoved = await LoadPromotionAsync(session.Session);
        if (promoRemoved is not null)
        {
            // The code stopped applying since the page was opened: show the new amount before charging it.
            PromoError = promoRemoved;
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }
        await LoadReservationDepositCreditAsync(session.Session);
        ViewData["CartItemCount"] = await _diningSessions.GetCartItemCountAsync(session.Session.Id);
        var payableOrders = SessionOrders.Where(order => order.IsPayable).ToList();
        if (payableOrders.Count == 0)
        {
            StatusMessage = "There are no placed orders to pay for yet.";
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        if (payableOrders.Any(order => !order.IsServed))
        {
            StatusMessage = "Payment is available after all orders have been served.";
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        if (AmountDue == 0)
        {
            var activeSession = await _db.Sessions
                .Include(active => active.Orders)
                .SingleOrDefaultAsync(active =>
                    active.Id == session.Session.Id && active.EndingTime == null);
            if (activeSession is null)
            {
                return RedirectToPage("/Table/Index", new { tableId = TableId });
            }

            foreach (var order in activeSession.Orders.Where(order =>
                         order.Status != OrderStatus.Cancelled &&
                         order.Status != OrderStatus.Completed))
            {
                order.Status = OrderStatus.Completed;
            }

            activeSession.EndingTime = DateTime.UtcNow;
            var creditRemaining = (int)(ReservationDepositCredit * 100m);
            var promoRemaining = (int)(PromoDiscount * 100m);
            foreach (var order in activeSession.Orders
                         .Where(order => order.Status != OrderStatus.Cancelled)
                         .OrderBy(order => order.Id, StringComparer.Ordinal))
            {
                // Same split as the Stripe payment page: the promo discount first, then the deposit.
                var orderPromo = Math.Min(order.TotalAmount, promoRemaining);
                var orderCredit = Math.Min(order.TotalAmount - orderPromo, creditRemaining);
                _db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OrderId = order.Id,
                    Amount = order.TotalAmount - orderPromo - orderCredit,
                    Currency = Currency,
                    Status = TransactionStatus.Succeeded,
                    PaymentMethod = PaymentMethod.Other,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        reservationDepositCredit = orderCredit,
                        promoDiscount = orderPromo,
                        promotionCode = orderPromo > 0 ? AppliedPromotion?.Code : null
                    })
                });
                creditRemaining -= orderCredit;
                promoRemaining -= orderPromo;
            }

            var cartItems = await _db.CartItems
                .Where(item => item.SessionId == activeSession.Id)
                .ToListAsync();
            _db.CartItems.RemoveRange(cartItems);
            await _diningSessions.CompleteCheckedInReservationsAsync(activeSession);
            await _db.SaveChangesAsync();

            IsSessionClosed = true;
            StatusMessage = PromoDiscount > 0 && ReservationDepositCredit == 0
                ? "Your promo code covered the full order total. Checkout is complete."
                : "Your discounts covered the full order total. Checkout is complete.";
            return Page();
        }

        var items = payableOrders
            .SelectMany(order => order.Items)
            .Select(item => (
                item.Name,
                item.UnitPrice,
                item.Quantity,
                $"{item.Options}{(string.IsNullOrWhiteSpace(item.Remark) ? string.Empty : $"; {item.Remark}")}"));
        var paymentAttemptId = string.Join(",", payableOrders
            .Select(order => order.OrderId)
            .OrderBy(orderCode => orderCode, StringComparer.Ordinal));
        var domain = $"{Request.Scheme}://{Request.Host}";

        // A promo code or deposit can leave less than Stripe will charge; that remainder is paid at the counter.
        var stripeMinimumCharge = GetStripeMinimumCharge(Currency);
        if (AmountDue < stripeMinimumCharge)
        {
            StatusMessage = $"The amount left to pay ({CurrencySymbol} {AmountDue:F2}) is below the {CurrencySymbol} {stripeMinimumCharge:F2} minimum for card payments. Please pay it at the counter.";
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        Stripe.Checkout.Session stripeSession;
        try
        {
            stripeSession = await _paymentService.CreateTableSettlementSessionAsync(
                TableId,
                session.Session.Id,
                paymentAttemptId,
                items,
                (int)(ReservationDepositCredit * 100m),
                (int)(PromoDiscount * 100m),
                PromoDiscount > 0 ? AppliedPromotion?.Code : null,
                Currency,
                domain);
        }
        catch (Stripe.StripeException error)
        {
            _logger.LogError(error, "Stripe checkout could not be created for table {TableId}.", TableId);
            StatusMessage = "We couldn't start the card payment just now. Please try again, or pay at the counter.";
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        return Redirect(stripeSession.Url);
    }

    public async Task<IActionResult> OnPostCancelOrderAsync(string tableId, string orderId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        var cancelled = await _db.Orders
            .Where(order =>
                order.Id == orderId &&
                order.SessionId == session.Session!.Id &&
                order.Status == OrderStatus.InQueue)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(order => order.Status, OrderStatus.Cancelled));

        if (cancelled == 0)
        {
            var orderExists = await _db.Orders.AnyAsync(order =>
                order.Id == orderId && order.SessionId == session.Session!.Id);
            StatusMessage = orderExists
                ? "This order is no longer pending, so it cannot be cancelled."
                : "That order was not found in this dining session.";
        }
        else
        {
            StatusMessage = "Your order has been cancelled.";
        }

        return RedirectToPage("/Table/Checkout", new { tableId = TableId });
    }

    public async Task<IActionResult> OnPostApplyPromoCodeAsync(string tableId, string? promoCode)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        var code = string.Concat((promoCode ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (code.Length == 0)
        {
            PromoError = "Enter a promo code first.";
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        var promotion = await _db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code);
        await LoadSessionOrdersAsync(session.Session!.Id);
        var subtotal = (int)(TotalAmount * 100m);

        PromoError = promotion is null || promotion.DiscountType is null
                ? $"\"{code}\" isn't a valid promo code."
            : promotion.StartDate > _clock.Today
                ? $"{code} can be used from {promotion.StartDate:d MMM yyyy}."
            : promotion.EndDate < _clock.Today
                ? $"{code} expired on {promotion.EndDate:d MMM yyyy}."
            : subtotal < promotion.MinimumSpend
                ? $"{code} needs a minimum spend of {CurrencySymbol} {promotion.MinimumSpend / 100m:F2}. Your orders come to {CurrencySymbol} {TotalAmount:F2}."
            : null;
        if (PromoError is not null)
        {
            return RedirectToPage("/Table/Checkout", new { tableId = TableId });
        }

        await _db.Sessions
            .Where(active => active.Id == session.Session.Id && active.EndingTime == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(active => active.PromotionId, promotion!.Id));
        PromoMessage = $"Promo code {code} applied.";
        return RedirectToPage("/Table/Checkout", new { tableId = TableId });
    }

    public async Task<IActionResult> OnPostRemovePromoCodeAsync(string tableId)
    {
        var session = await GetActiveSessionAsync(tableId);
        if (session.Result is not null)
        {
            return session.Result;
        }

        await ClearPromotionAsync(session.Session!.Id);
        PromoMessage = "Promo code removed.";
        return RedirectToPage("/Table/Checkout", new { tableId = TableId });
    }

    // Loads the promotion this table applied and works out its discount on the current orders total.
    // A code that no longer applies (deleted, outside its dates, or its discount removed) is taken off the
    // session, and the message saying why is returned; null when nothing was removed.
    private async Task<string?> LoadPromotionAsync(RestaurantReservation.Models.Session session)
    {
        AppliedPromotion = null;
        PromoDiscount = 0;
        if (session.PromotionId is null)
        {
            return null;
        }

        var promotion = await _db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.PromotionId);
        if (promotion is null || promotion.DiscountType is null || !promotion.IsRunningOn(_clock.Today))
        {
            await ClearPromotionAsync(session.Id);
            return promotion is null || promotion.DiscountType is null
                ? "The promo code you applied is no longer available, so it has been removed."
                : $"Promo code {promotion.Code} is no longer running, so it has been removed.";
        }

        AppliedPromotion = promotion;
        PromoDiscount = promotion.DiscountOn((int)(TotalAmount * 100m)) / 100m;
        return null;
    }

    private Task ClearPromotionAsync(string sessionId) =>
        _db.Sessions
            .Where(active => active.Id == sessionId && active.EndingTime == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(active => active.PromotionId, (string?)null));

    private static decimal GetStripeMinimumCharge(CurrencyType currency) => currency switch
    {
        CurrencyType.MYR => 2.00m,
        CurrencyType.USD or CurrencyType.EUR => 0.50m,
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unsupported Stripe currency.")
    };

    private async Task<(RestaurantReservation.Models.Session? Session, IActionResult? Result)>
        GetActiveSessionAsync(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return (null, NotFound());
        }

        TableId = tableId.ToUpperInvariant();
        var session = await _diningSessions.GetActiveSessionAsync(TableId);
        if (session is null)
        {
            return (null, RedirectToPage("/Table/Index", new { tableId = TableId }));
        }

        return (session, null);
    }

    private async Task LoadSessionOrdersAsync(string sessionId)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(order => order.SessionId == sessionId)
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.MenuItem)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();

        SessionOrders = orders.Select(order => new SessionOrderViewModel
        {
            OrderId = order.Id,
            OrderCode = order.Id[..Math.Min(order.Id.Length, 8)].ToUpperInvariant(),
            FormattedTime = order.CreatedAt.ToLocalTime().ToString("g"),
            Status = order.Status.ToString(),
            TotalAmount = order.TotalAmount / 100m,
            CanCancel = order.Status == OrderStatus.InQueue,
            IsPayable = order.Status != OrderStatus.Cancelled,
            IsServed = order.Status == OrderStatus.Served,
            Items = order.OrderItems.Select(item => new CheckoutOrderItem
            {
                Name = item.ItemName,
                UnitPrice = item.UnitPrice / 100m,
                Quantity = item.Quantity,
                Remark = item.Remarks,
                Options = string.Join(", ", item.Options.Select(option => $"{option.Key}: {option.Value}"))
            }).ToList()
        }).ToList();
    }

    private async Task LoadReservationDepositCreditAsync(RestaurantReservation.Models.Session session)
    {
        ReservationDepositCredit = 0;
        if (session.CustomerId is null)
        {
            return;
        }

        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(item => item.ReservationTables)
            .Where(item =>
                item.Status == ReservationStatus.CheckedIn &&
                item.DepositStatus == DepositStatus.Paid &&
                item.CustomerId == session.CustomerId &&
                item.ReservationTables.Any(link => link.TableId == session.TableId))
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync();
        if (reservation is null)
        {
            return;
        }

        var tableIds = reservation.ReservationTables
            .Select(link => link.TableId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var activeSessions = await _db.Sessions
            .AsNoTracking()
            .Where(active =>
                active.CustomerId == reservation.CustomerId &&
                active.EndingTime == null &&
                tableIds.Contains(active.TableId) &&
                active.Orders.Any(order => order.Status != OrderStatus.Cancelled))
            .Select(active => active.TableId)
            .Distinct()
            .ToListAsync();
        var creditTableId = activeSessions
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!string.Equals(creditTableId, session.TableId, StringComparison.Ordinal))
        {
            return;
        }

        var reservationOrderTotals = await _db.Orders
            .AsNoTracking()
            .Where(order =>
                order.Session!.CustomerId == reservation.CustomerId &&
                tableIds.Contains(order.Session.TableId) &&
                order.Status != OrderStatus.Cancelled)
            .Select(order => new { order.Id, order.TotalAmount })
            .ToListAsync();
        var reservationOrderIds = reservationOrderTotals.Select(order => order.Id).ToArray();
        var successfulPayments = (await _db.Transactions
                .AsNoTracking()
                .Where(transaction =>
                    reservationOrderIds.Contains(transaction.OrderId) &&
                    transaction.Status == TransactionStatus.Succeeded)
                .Select(transaction => new { transaction.OrderId, transaction.Amount, transaction.MetadataJson })
                .ToListAsync())
            .GroupBy(transaction => transaction.OrderId)
            .ToDictionary(
                group => group.Key,
                group => (
                    AmountPaid: group.Sum(transaction => transaction.Amount),
                    PromoDiscount: group.Sum(transaction => ReadPromoDiscount(transaction.MetadataJson))));
        var alreadyApplied = reservationOrderTotals
            .Where(order => successfulPayments.ContainsKey(order.Id))
            .Sum(order => Math.Max(
                0,
                order.TotalAmount
                    - Math.Min(order.TotalAmount, successfulPayments[order.Id].AmountPaid)
                    - successfulPayments[order.Id].PromoDiscount));
        var depositAmount = reservation.DepositAmount ?? 0;
        var orderTotal = (int)((TotalAmount - PromoDiscount) * 100m);
        var remainingDeposit = Math.Max(0, depositAmount - alreadyApplied);
        ReservationDepositCredit = Math.Min(remainingDeposit, orderTotal) / 100m;
    }

    // The promo discount (in cents) recorded on a transaction; 0 for transactions without one.
    private static int ReadPromoDiscount(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return 0;
        }

        try
        {
            using var metadata = System.Text.Json.JsonDocument.Parse(metadataJson);
            return metadata.RootElement.TryGetProperty("promoDiscount", out var value) && value.TryGetInt32(out var amount)
                ? amount
                : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

}
