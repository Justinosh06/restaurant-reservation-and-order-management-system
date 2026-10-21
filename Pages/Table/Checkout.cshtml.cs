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
    public List<SessionOrderViewModel> SessionOrders { get; private set; } = [];
    public decimal TotalAmount => SessionOrders
        .Where(order => order.IsPayable)
        .Sum(order => order.TotalAmount);
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

        await LoadSessionOrdersAsync(session.Session!.Id);
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
            domain);

        return Redirect(stripeSession.Url);
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

}
