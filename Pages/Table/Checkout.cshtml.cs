using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
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

    public CheckoutModel(
        ApplicationDbContext db,
        IPaymentService paymentService,
        DiningSessionService diningSessions)
    {
        _db = db;
        _paymentService = paymentService;
        _diningSessions = diningSessions;
    }

    public string TableId { get; private set; } = string.Empty;
    public bool IsCustomerBound { get; private set; }
    public bool IsSessionClosed { get; private set; }
    public List<SessionOrderViewModel> SessionOrders { get; private set; } = [];
    public decimal TotalAmount => SessionOrders
        .Where(order => order.IsPayable)
        .Sum(order => order.TotalAmount);
    public decimal ReservationDepositCredit { get; private set; }
    public decimal AmountDue => Math.Max(0, TotalAmount - ReservationDepositCredit);
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

        IsCustomerBound = session.Session!.CustomerId is not null;
        await LoadSessionOrdersAsync(session.Session!.Id);
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

        IsCustomerBound = session.Session!.CustomerId is not null;
        await LoadSessionOrdersAsync(session.Session!.Id);
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
            foreach (var order in activeSession.Orders
                         .Where(order => order.Status != OrderStatus.Cancelled)
                         .OrderBy(order => order.Id, StringComparer.Ordinal))
            {
                var orderCredit = Math.Min(order.TotalAmount, creditRemaining);
                _db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OrderId = order.Id,
                    Amount = order.TotalAmount - orderCredit,
                    Currency = RestaurantReservation.Models.Enums.CurrencyType.MYR,
                    Status = TransactionStatus.Succeeded,
                    PaymentMethod = PaymentMethod.Other,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        reservationDepositCredit = orderCredit
                    })
                });
                creditRemaining -= orderCredit;
            }

            var cartItems = await _db.CartItems
                .Where(item => item.SessionId == activeSession.Id)
                .ToListAsync();
            _db.CartItems.RemoveRange(cartItems);
            await _diningSessions.CompleteCheckedInReservationsAsync(activeSession);
            await _db.SaveChangesAsync();

            IsSessionClosed = true;
            StatusMessage = "Your paid reservation deposit covered the full order total. Checkout is complete.";
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

        var stripeSession = await _paymentService.CreateTableSettlementSessionAsync(
            TableId,
            session.Session.Id,
            paymentAttemptId,
            items,
            (int)(ReservationDepositCredit * 100m),
            domain);

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
        var successfulPayments = await _db.Transactions
            .AsNoTracking()
            .Where(transaction =>
                reservationOrderIds.Contains(transaction.OrderId) &&
                transaction.Status == TransactionStatus.Succeeded)
            .GroupBy(transaction => transaction.OrderId)
            .Select(group => new
            {
                OrderId = group.Key,
                AmountPaid = group.Sum(transaction => transaction.Amount)
            })
            .ToDictionaryAsync(payment => payment.OrderId, payment => payment.AmountPaid);
        var alreadyApplied = reservationOrderTotals
            .Where(order => successfulPayments.ContainsKey(order.Id))
            .Sum(order => Math.Max(
                0,
                order.TotalAmount - Math.Min(order.TotalAmount, successfulPayments[order.Id])));
        var depositAmount = reservation.DepositAmount ?? 0;
        var orderTotal = (int)(TotalAmount * 100m);
        var remainingDeposit = Math.Max(0, depositAmount - alreadyApplied);
        ReservationDepositCredit = Math.Min(remainingDeposit, orderTotal) / 100m;
    }

}
