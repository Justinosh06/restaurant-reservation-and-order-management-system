using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantReservation.Services;

namespace RestaurantReservation.Pages.Table;

public class CheckoutOrderItem
{
    public string Name { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string Remark { get; set; } = string.Empty;
}

public class SessionOrderViewModel
{
    public string OrderCode { get; set; } = string.Empty;
    public string FormattedTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<CheckoutOrderItem> Items { get; set; } = new();
}

public class CheckoutModel : PageModel
{
    private readonly IPaymentService _paymentService;

    public CheckoutModel(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public string TableId { get; set; } = string.Empty;
    public List<SessionOrderViewModel> SessionOrders { get; set; } = new();
    public double TotalAmount { get; set; }

    public IActionResult OnGet(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId)) return NotFound();
        TableId = tableId.ToUpper();
        LoadSessionOrders();
        return Page();
    }

    public async Task<IActionResult> OnPostPayWithStripeAsync(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId)) return NotFound();

        TableId = tableId.ToUpper();
        LoadSessionOrders();

        if (!SessionOrders.Any())
        {
            return RedirectToPage("/Table/Index", new { tableId = TableId });
        }

        // Prepare simplified item tuple list
        var items = SessionOrders
            .SelectMany(o => o.Items)
            .Select(i => (i.Name, i.UnitPrice, i.Quantity, i.Remark));

        var domain = $"{Request.Scheme}://{Request.Host}";

        // Delegate session creation to centralized service
        var session = await _paymentService.CreateTableSettlementSessionAsync(TableId, items, domain);

        return Redirect(session.Url);
    }

    private void LoadSessionOrders()
    {
        // Mock session orders for this table
        SessionOrders = new List<SessionOrderViewModel>
        {
            new SessionOrderViewModel
            {
                OrderCode = "2610072006",
                FormattedTime = "Today 20:06",
                Status = "Preparing",
                Items = new List<CheckoutOrderItem>
                {
                    new CheckoutOrderItem { Name = "Spaghetti Bolognaise", UnitPrice = 24.90, Quantity = 2, Remark = "Extra cheese" },
                    new CheckoutOrderItem { Name = "Iced Cappuccino", UnitPrice = 12.90, Quantity = 1 }
                }
            },
            new SessionOrderViewModel
            {
                OrderCode = "2610072045",
                FormattedTime = "Today 20:45",
                Status = "Served",
                Items = new List<CheckoutOrderItem>
                {
                    new CheckoutOrderItem { Name = "Matcha Latte", UnitPrice = 14.00, Quantity = 1 }
                }
            }
        };

        TotalAmount = SessionOrders.SelectMany(o => o.Items).Sum(i => i.UnitPrice * i.Quantity);
    }
}