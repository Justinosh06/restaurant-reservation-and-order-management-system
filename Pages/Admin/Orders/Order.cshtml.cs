using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Extensions;
using RestaurantReservation.Infrastructure.Admin;
using RestaurantReservation.Infrastructure.Payments;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Admin.Orders;

public class OrderModel : PageModel
{
    private static readonly OrderStatus[] ActiveStatuses = [OrderStatus.InQueue, OrderStatus.Preparing, OrderStatus.Served];

    private readonly ApplicationDbContext _db;
    private readonly RestaurantClock _clock;
    private readonly StripePaymentDetails _stripe;
    private readonly DiningSessionService _diningSessions;

    public OrderModel(
        ApplicationDbContext db,
        RestaurantClock clock,
        StripePaymentDetails stripe,
        DiningSessionService diningSessions)
    {
        _db = db;
        _clock = clock;
        _stripe = stripe;
        _diningSessions = diningSessions;
    }

    public DateOnly Today => _clock.Today;

    // null when today's opening hours haven't been set in Restaurant Settings.
    public bool? IsOpenNow { get; private set; }

    public async Task OnGetAsync()
    {
        var now = _clock.Now;
        var hours = await _db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(h => h.DayOfWeek == now.DayOfWeek);
        if (hours is null)
        {
            return;
        }

        if (hours.IsClosed || hours.OpenTime is null || hours.CloseTime is null)
        {
            IsOpenNow = false;
            return;
        }

        var time = TimeOnly.FromDateTime(now);
        IsOpenNow = time.IsBetween(hours.OpenTime.Value, hours.CloseTime.Value);
    }

    public record AdvanceRequest(string Id, string? ExpectedStatus);

    public record OrderItemRow(string Name, int Quantity, int UnitPrice, string? Remarks);

    public record OrderRow(
        string Id,
        string DisplayId,
        string SessionId,
        string? Table,
        string Customer,
        List<OrderItemRow> Items,
        DateTime CreatedAt,
        int Total,
        string Status,
        bool CashPaymentPending,
        int CashAmountCents,
        int CashSessionAmountCents);

    public record CashPaymentRequest(string SessionId, decimal CashTendered, string CustomerEmail);

    public async Task<IActionResult> OnGetListAsync()
    {
        var activeOrders = _db.Orders.Where(o => ActiveStatuses.Contains(o.Status));
        return new JsonResult(await ToRowsAsync(activeOrders));
    }

    public record VisitRow(
        string Id,
        string? Table,
        string Customer,
        List<string> OrderIds,
        List<OrderItemRow> Items,
        DateTime StartedAt,
        DateTime LastOrderAt,
        int Total,
        int PromoDiscount,
        string? PromotionCode,
        int ReservationDepositCredit);

    public record PaymentDetailsRow(
        int AmountCents,
        string CurrencySymbol,
        string State,
        string? CheckoutSessionId,
        string? PaymentIntentId,
        string MethodLabel,
        string CustomerName,
        string? CustomerEmail,
        DateTime? PaidAtUtc,
        DateTime? RefundedAtUtc,
        string? ReceiptUrl,
        int? CashTenderedCents,
        int? ChangeCents);

    // History only lists finished orders: served to the table and paid for (status Completed).
    // Orders are grouped by dining session, so a table that ordered twice in one visit shows as one bill.
    // A visit is listed when any of its orders falls in the dates, and then shows all of its orders,
    // so a visit that ran past midnight isn't cut in two by the date range.
    public async Task<IActionResult> OnGetHistoryAsync(string? from, string? to)
    {
        var dateError = DateRangeInput.Check(from, to, out var fromDate, out var toDate);
        if (dateError is not null)
        {
            return BadRequest(new { error = dateError });
        }

        var startUtc = _clock.StartOfDayUtc(fromDate);
        var endUtc = _clock.StartOfDayUtc(toDate.AddDays(1));
        var completed = _db.Orders.Where(o => o.Status == OrderStatus.Completed);
        var sessionIds = completed
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .Select(o => o.SessionId);
        var orders = await completed.AsNoTracking()
            .Where(o => sessionIds.Contains(o.SessionId))
            .Select(o => new
            {
                o.Id,
                o.SessionId,
                o.TotalAmount,
                o.CreatedAt,
                o.Session!.TableId,
                OrderEmail = o.Customer!.Email,
                SessionEmail = o.Session.Customer!.Email
            })
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        // Name and price as charged on the order, not today's menu, which can since have been edited.
        var orderItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => orderIds.Contains(oi.OrderId))
            .Select(oi => new { oi.OrderId, oi.ItemName, oi.UnitPrice, oi.Quantity, oi.Remarks })
            .ToListAsync();
        var itemsByOrder = orderItems.ToLookup(oi => oi.OrderId);

        var transactions = await _db.Transactions.AsNoTracking()
            .Where(t => orderIds.Contains(t.OrderId)
                && (t.Status == TransactionStatus.Succeeded || t.Status == TransactionStatus.Refunded))
            .Select(t => new { t.OrderId, t.MetadataJson })
            .ToListAsync();
        var transactionsByOrder = transactions.ToLookup(t => t.OrderId);

        var visits = orders
            .GroupBy(o => o.SessionId)
            .Select(visit =>
            {
                var visitOrders = visit.OrderBy(o => o.CreatedAt).ToList();
                var visitItems = visitOrders.SelectMany(o => itemsByOrder[o.Id]).ToList();

                // The same dish at the same price ordered in both rounds becomes one line with the quantities added up;
                // a different price (another size, or a price change between rounds) keeps its own line.
                var items = visitItems
                    .GroupBy(oi => new { oi.ItemName, oi.UnitPrice })
                    .Select(dish => new OrderItemRow(
                        dish.Key.ItemName,
                        dish.Sum(oi => oi.Quantity),
                        dish.Key.UnitPrice,
                        JoinRemarks(dish.Select(oi => oi.Remarks))))
                    .ToList();

                var customers = visitOrders
                    .Select(o => o.OrderEmail ?? o.SessionEmail)
                    .Where(email => !string.IsNullOrEmpty(email))
                    .Distinct()
                    .ToList();

                var visitTransactions = visitOrders.SelectMany(o => transactionsByOrder[o.Id]).ToList();
                var promoDiscount = visitTransactions.Sum(t => ReadCents(t.MetadataJson, "promoDiscount"));
                var depositCredit = visitTransactions.Sum(t => ReadCents(t.MetadataJson, "reservationDepositCredit"));
                var promotionCode = visitTransactions
                    .Select(t => ReadText(t.MetadataJson, "promotionCode"))
                    .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));

                return new VisitRow(
                    visit.Key,
                    visitOrders[0].TableId,
                    customers.Count == 0 ? "Walk-in guest" : string.Join(", ", customers),
                    visitOrders.Select(o => AdminDisplay.ShortId(o.Id)).ToList(),
                    items,
                    visitOrders[0].CreatedAt,
                    visitOrders[^1].CreatedAt,
                    visitOrders.Sum(o => o.TotalAmount),
                    promoDiscount,
                    promotionCode,
                    depositCredit);
            })
            .OrderByDescending(v => v.LastOrderAt)
            .ToList();

        return new JsonResult(visits);
    }

    private static int ReadCents(string? metadataJson, string name) =>
        ReadMetadata(metadataJson, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var cents)
            ? cents
            : 0;

    private static string? ReadText(string? metadataJson, string name) =>
        ReadMetadata(metadataJson, name) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : null;

    private static JsonElement? ReadMetadata(string? metadataJson, string name)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(metadataJson);
            return json.RootElement.TryGetProperty(name, out var value) ? value.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IActionResult> OnGetPaymentDetailsAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "A dining session is required." });
        }

        var orderIds = await _db.Orders.AsNoTracking()
            .Where(o => o.SessionId == id && o.Status == OrderStatus.Completed)
            .Select(o => o.Id)
            .ToListAsync();
        if (orderIds.Count == 0)
        {
            return NotFound();
        }

        var transactions = await _db.Transactions.AsNoTracking()
            .Include(t => t.Order!.Customer)
            .Where(t => orderIds.Contains(t.OrderId)
                && (t.Status == TransactionStatus.Succeeded || t.Status == TransactionStatus.Refunded))
            .ToListAsync();

        var groups = transactions
            .GroupBy(t => t.StripeCheckoutSessionId
                ?? (t.PaymentMethod == PaymentMethod.Cash
                    ? $"cash:{ReadText(t.MetadataJson, "cashPaymentGroupId") ?? t.Order!.SessionId}"
                    : t.Id))
            .OrderBy(g => g.Min(t => t.CreatedAt))
            .ToList();
        var stripePayments = await Task.WhenAll(groups.Select(group => _stripe.GetAsync(
            group.First().StripeCheckoutSessionId,
            expectRefunded: group.Any(t => t.Status == TransactionStatus.Refunded))));

        var payments = groups.Select((group, index) =>
        {
            var first = group.FirstOrDefault(transaction =>
                ReadCents(transaction.MetadataJson, "cashTendered") > 0) ?? group.First();
            var stripe = stripePayments[index];
            var refunded = stripe?.Refunded == true || group.Any(t => t.Status == TransactionStatus.Refunded);

            return new PaymentDetailsRow(
                group.Sum(t => t.Amount),
                first.Currency.GetSymbol(),
                refunded ? "Refunded" : "Paid",
                first.StripeCheckoutSessionId,
                stripe?.PaymentIntentId,
                stripe?.MethodLabel ?? first.PaymentMethod.ToString(),
                stripe?.CustomerName ?? first.Order?.Customer?.FullName ?? "Guest",
                stripe?.CustomerEmail ?? first.Order?.Customer?.Email,
                stripe?.PaidAt ?? first.CreatedAt,
                stripe?.RefundedAt,
                first.PaymentMethod == PaymentMethod.Cash
                    ? $"/Admin/Orders?handler=CashReceipt&sessionId={Uri.EscapeDataString(first.Order!.SessionId)}&paymentGroupId={Uri.EscapeDataString(ReadText(first.MetadataJson, "cashPaymentGroupId") ?? string.Empty)}"
                    : stripe?.ReceiptUrl,
                first.PaymentMethod == PaymentMethod.Cash
                    ? ReadCents(first.MetadataJson, "cashTendered")
                    : null,
                first.PaymentMethod == PaymentMethod.Cash
                    ? ReadCents(first.MetadataJson, "changeDue")
                    : null);
        }).ToList();

        return new JsonResult(payments);
    }

    private static string? JoinRemarks(IEnumerable<string?> remarks)
    {
        var distinct = remarks.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()).Distinct().ToList();
        return distinct.Count == 0 ? null : string.Join("; ", distinct);
    }

    private async Task<List<OrderRow>> ToRowsAsync(IQueryable<Order> query)
    {
        var orders = await query.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.TotalAmount,
                o.Status,
                o.CreatedAt,
                o.SessionId,
                o.Session!.TableId,
                CustomerEmail = o.Customer == null ? null : o.Customer.Email
            })
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var cashTransactions = await _db.Transactions.AsNoTracking()
            .Where(transaction =>
                orderIds.Contains(transaction.OrderId) &&
                transaction.Status == TransactionStatus.Pending &&
                transaction.PaymentMethod == PaymentMethod.Cash)
            .Select(transaction => new { transaction.OrderId, transaction.Amount })
            .ToListAsync();
        var cashByOrder = cashTransactions.ToDictionary(transaction => transaction.OrderId);
        var cashTotalsBySession = orders
            .Where(order => cashByOrder.ContainsKey(order.Id))
            .GroupBy(order => order.SessionId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(order => cashByOrder[order.Id].Amount));

        var orderItems = await _db.OrderItems.AsNoTracking()
            .Where(oi => orderIds.Contains(oi.OrderId))
            .Select(oi => new { oi.OrderId, oi.ItemName, oi.UnitPrice, oi.Quantity, oi.Remarks })
            .ToListAsync();
        var items = orderItems.ToLookup(oi => oi.OrderId, oi => new OrderItemRow(oi.ItemName, oi.Quantity, oi.UnitPrice, oi.Remarks));

        return orders.Select(o => new OrderRow(
            o.Id,
            AdminDisplay.ShortId(o.Id),
            o.SessionId,
            o.TableId,
            o.CustomerEmail ?? "Walk-in guest",
            items[o.Id].ToList(),
            o.CreatedAt,
            o.TotalAmount,
            AdminDisplay.OrderStatusLabel(o.Status),
            cashByOrder.TryGetValue(o.Id, out var cashTransaction),
            cashTransaction?.Amount ?? 0,
            cashTotalsBySession.GetValueOrDefault(o.SessionId))).ToList();
    }

    public async Task<IActionResult> OnPostAdvanceAsync([FromBody] AdvanceRequest request)
    {
        var expected = request.ExpectedStatus switch
        {
            "Pending" => OrderStatus.InQueue,
            "Preparing" => OrderStatus.Preparing,
            _ => (OrderStatus?)null
        };
        if (expected is null)
        {
            return BadRequest(new { error = "This order can't be advanced from its current status." });
        }

        var next = expected.Value switch
        {
            OrderStatus.InQueue => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.Served,
            _ => throw new InvalidOperationException("An active order status has no next state.")
        };

        var servedAt = DateTime.UtcNow;
        var affected = await _db.Orders
            .Where(order => order.Id == request.Id && order.Status == expected.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.Status, next)
                .SetProperty(order => order.ServedAt, order => next == OrderStatus.Served ? servedAt : order.ServedAt));

        if (affected == 0)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(order => order.Id == request.Id);
            if (order is null)
            {
                return NotFound(new { error = "This order no longer exists. Refresh the page." });
            }

            var current = AdminDisplay.OrderStatusLabel(order.Status);
            return new ConflictObjectResult(new { error = $"This order was already moved to {current} by someone else.", id = order.Id, status = current });
        }

        return new JsonResult(new { id = request.Id, status = AdminDisplay.OrderStatusLabel(next) });
    }

    public async Task<IActionResult> OnGetCashOptionsAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "A dining session is required." });
        }

        var session = await _db.Sessions.AsNoTracking()
            .Where(diningSession => diningSession.Id == sessionId && diningSession.EndingTime == null)
            .Select(diningSession => new
            {
                diningSession.Id,
                diningSession.TableId,
                CustomerEmail = diningSession.Customer!.Email
            })
            .SingleOrDefaultAsync();
        if (session is null)
        {
            return NotFound(new { error = "This dining session is no longer active." });
        }

        var pendingPayments = await _db.Transactions.AsNoTracking()
            .Include(transaction => transaction.Order!.OrderItems)
            .Where(transaction =>
                transaction.Order!.SessionId == sessionId &&
                transaction.PaymentMethod == PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Pending)
            .ToListAsync();
        if (pendingPayments.Count == 0)
        {
            return NotFound(new { error = "There is no cash payment waiting for this table." });
        }

        return new JsonResult(new
        {
            sessionId = session.Id,
            tableId = session.TableId,
            customerEmail = session.CustomerEmail,
            amountDueCents = pendingPayments.Sum(payment => payment.Amount),
            orders = pendingPayments
                .OrderBy(payment => payment.Order!.CreatedAt)
                .Select(payment => new
                {
                    id = payment.Order!.Id,
                    displayId = payment.Order.Id[..Math.Min(payment.Order.Id.Length, 8)].ToUpperInvariant(),
                    amountDueCents = payment.Amount,
                    items = payment.Order.OrderItems
                        .Select(item => new
                        {
                            item.ItemName,
                            item.Quantity,
                            item.UnitPrice
                        })
                        .ToList()
                })
                .ToList(),
            customerEmails = await _db.Customers.AsNoTracking()
                .OrderBy(customer => customer.Email)
                .Select(customer => customer.Email)
                .ToListAsync()
        });
    }

    public async Task<IActionResult> OnPostRecordCashAsync([FromBody] CashPaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return BadRequest(new { error = "A dining session is required." });
        }

        if (!MailAddress.TryCreate(request.CustomerEmail?.Trim(), out var parsedEmail))
        {
            return BadRequest(new { error = "Enter a valid customer email address." });
        }

        await using var databaseTransaction = await _db.Database.BeginTransactionAsync();
        var session = await _db.Sessions
            .Include(diningSession => diningSession.Orders)
            .SingleOrDefaultAsync(diningSession =>
                diningSession.Id == request.SessionId && diningSession.EndingTime == null);
        if (session is null)
        {
            return new ConflictObjectResult(new { error = "This dining session is no longer active." });
        }

        var pendingPayments = await _db.Transactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.Order!.SessionId == session.Id &&
                transaction.PaymentMethod == PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Pending)
            .Select(transaction => new { transaction.Id, transaction.OrderId, transaction.Amount })
            .ToListAsync();
        if (pendingPayments.Count == 0)
        {
            return new ConflictObjectResult(new { error = "There is no cash payment waiting for this table." });
        }

        var paymentOrderIds = pendingPayments.Select(payment => payment.OrderId).ToHashSet(StringComparer.Ordinal);
        var paymentOrders = session.Orders.Where(order => paymentOrderIds.Contains(order.Id)).ToList();
        if (paymentOrders.Count != paymentOrderIds.Count ||
            paymentOrders.Any(order => order.Status != OrderStatus.Served))
        {
            return new ConflictObjectResult(new { error = "Cash can only be recorded for orders that have all been served." });
        }

        var customerEmail = parsedEmail.Address.Trim().ToLowerInvariant();
        var customer = await _db.Customers
            .FirstOrDefaultAsync(account => account.Email.ToLower() == customerEmail);
        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid().ToString("N"),
                Email = customerEmail
            };
            _db.Customers.Add(customer);
        }

        var dueCents = pendingPayments.Sum(payment => payment.Amount);
        var tenderedCentsDecimal = request.CashTendered * 100m;
        if (tenderedCentsDecimal != decimal.Truncate(tenderedCentsDecimal) ||
            tenderedCentsDecimal < dueCents ||
            tenderedCentsDecimal > int.MaxValue)
        {
            return BadRequest(new
            {
                error = tenderedCentsDecimal < dueCents
                    ? "The cash received must be at least the amount due."
                    : "Enter a cash amount with no more than two decimal places."
            });
        }

        var tenderedCents = (int)tenderedCentsDecimal;
        var changeCents = tenderedCents - dueCents;
        var paidAt = DateTime.UtcNow;
        var paymentIds = pendingPayments.Select(payment => payment.Id).ToArray();
        var tenderedPaymentOrderId = pendingPayments.MinBy(payment => payment.OrderId)!.OrderId;
        var paymentGroupId = Guid.NewGuid().ToString("N");
        var claimedPayments = await _db.Transactions
            .Where(transaction =>
                paymentIds.Contains(transaction.Id) &&
                transaction.PaymentMethod == PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Pending)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(transaction => transaction.Status, TransactionStatus.Processing));
        if (claimedPayments != pendingPayments.Count)
        {
            await databaseTransaction.RollbackAsync();
            return new ConflictObjectResult(new { error = "This cash payment was already recorded. Refresh the orders." });
        }

        var claimedTransactions = await _db.Transactions
            .Where(transaction => paymentIds.Contains(transaction.Id))
            .ToListAsync();
        foreach (var transaction in claimedTransactions)
        {
            transaction.Status = TransactionStatus.Succeeded;
            transaction.CreatedAt = paidAt;
            transaction.MetadataJson = CashMetadata(
                transaction.MetadataJson,
                transaction.OrderId == tenderedPaymentOrderId
                    ? tenderedCents
                    : null,
                transaction.OrderId == tenderedPaymentOrderId
                    ? changeCents
                    : null,
                customerEmail,
                paymentGroupId);
        }

        foreach (var order in session.Orders)
        {
            order.CustomerId = customer.Id;
            if (paymentOrderIds.Contains(order.Id))
            {
                order.Status = OrderStatus.Completed;
            }
        }

        session.CustomerId = customer.Id;
        var sessionEnded = !session.Orders.Any(DiningSessionService.IsOpen);
        if (sessionEnded)
        {
            session.EndingTime = paidAt;
            var cartItems = await _db.CartItems
                .Where(item => item.SessionId == session.Id)
                .ToListAsync();
            _db.CartItems.RemoveRange(cartItems);
            await _diningSessions.CompleteCheckedInReservationsAsync(session);
        }

        await _db.SaveChangesAsync();
        await databaseTransaction.CommitAsync();

        var paymentStatusTransaction = claimedTransactions
            .OrderBy(transaction => transaction.OrderId, StringComparer.Ordinal)
            .First();
        return new JsonResult(new
        {
            sessionId = session.Id,
            sessionEnded,
            receiptUrl = $"/Admin/Orders?handler=CashReceipt&sessionId={Uri.EscapeDataString(session.Id)}&paymentGroupId={paymentGroupId}",
            paymentStatusUrl = $"/Payment/{Uri.EscapeDataString(paymentStatusTransaction.Id)}",
            amountDueCents = dueCents,
            cashTenderedCents = tenderedCents,
            changeCents
        });
    }

    public async Task<IActionResult> OnGetCashReceiptAsync(string sessionId, string? paymentGroupId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return NotFound();
        }

        var payments = await _db.Transactions.AsNoTracking()
            .Include(transaction => transaction.Order!.OrderItems)
            .Include(transaction => transaction.Order!.Customer)
            .Include(transaction => transaction.Order!.Session!.Customer)
            .Where(transaction =>
                transaction.Order!.SessionId == sessionId &&
                transaction.PaymentMethod == PaymentMethod.Cash &&
                transaction.Status == TransactionStatus.Succeeded &&
                transaction.StripeCheckoutSessionId == null)
            .OrderBy(transaction => transaction.CreatedAt)
            .ToListAsync();
        if (payments.Count == 0)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(paymentGroupId))
        {
            payments = payments
                .Where(payment => ReadText(payment.MetadataJson, "cashPaymentGroupId") == paymentGroupId)
                .ToList();
            if (payments.Count == 0)
            {
                return NotFound();
            }
        }

        var firstPayment = payments.OrderBy(payment => payment.OrderId, StringComparer.Ordinal).First();
        var diningSession = firstPayment.Order!.Session!;
        var settings = await _db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        var customerName = firstPayment.Order.Customer?.FullName;
        if (string.IsNullOrWhiteSpace(customerName))
        {
            customerName = diningSession.Customer?.FullName;
        }

        var customerEmail = firstPayment.Order.Customer?.Email ?? diningSession.Customer?.Email ?? "Not provided";
        var receipt = new CashReceiptData(
            settings?.Name ?? "Restaurant",
            settings?.Address ?? string.Empty,
            $"CASH-{(ReadText(firstPayment.MetadataJson, "cashPaymentGroupId") ?? firstPayment.Id)[..8].ToUpperInvariant()}",
            diningSession.TableId,
            string.IsNullOrWhiteSpace(customerName) ? "Walk-in guest" : customerName,
            customerEmail,
            firstPayment.Currency,
            payments.Max(payment => payment.CreatedAt),
            ReadCents(firstPayment.MetadataJson, "cashTendered"),
            ReadCents(firstPayment.MetadataJson, "changeDue"),
            payments.Select(payment => new CashReceiptOrder(
                payment.Order!.Id,
                payment.Order.CreatedAt,
                payment.Order.TotalAmount,
                ReadCents(payment.MetadataJson, "promoDiscount"),
                ReadCents(payment.MetadataJson, "reservationDepositCredit"),
                ReadText(payment.MetadataJson, "promotionCode"),
                payment.Amount,
                payment.Order.OrderItems
                    .Select(item => new CashReceiptItem(item.ItemName, item.Quantity, item.UnitPrice))
                    .ToList()))
                .ToList());

        return File(
            CashReceiptPdf.Write(receipt),
            "application/pdf",
            $"cash-receipt-{receipt.ReceiptNumber}.pdf");
    }

    private static string CashMetadata(
        string? existingMetadata,
        int? cashTenderedCents,
        int? changeCents,
        string customerEmail,
        string paymentGroupId)
    {
        var metadata = string.IsNullOrWhiteSpace(existingMetadata)
            ? new JsonObject()
            : JsonNode.Parse(existingMetadata) as JsonObject
                ?? throw new InvalidOperationException("Cash payment metadata must be a JSON object.");
        if (cashTenderedCents is { } tendered)
        {
            metadata["cashTendered"] = tendered;
            metadata["changeDue"] = changeCents;
        }
        metadata["cashCustomerEmail"] = customerEmail;
        metadata["cashPaymentGroupId"] = paymentGroupId;
        return metadata.ToJsonString();
    }
}
