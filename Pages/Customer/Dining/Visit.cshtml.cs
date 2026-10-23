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

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Customer.Dining;

// One dining visit: its times, the orders placed during it (each opens the order page) and how the bill was paid.
public class VisitModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly StripePaymentDetails _stripe;

    public VisitModel(ApplicationDbContext db, StripePaymentDetails stripe)
    {
        _db = db;
        _stripe = stripe;
    }

    // One card per Stripe payment of this visit's orders; usually the whole bill was paid at once.
    public IReadOnlyList<PaymentPanel> Payments { get; private set; } = [];

    public CurrencyType Currency { get; private set; } = CurrencyType.MYR;
    public DiningVisit Visit { get; private set; } = null!;
    public ILookup<string, OrderItem> OrderItems { get; private set; } = Enumerable.Empty<OrderItem>().ToLookup(i => i.OrderId);

    public async Task<IActionResult> OnGetAsync(string id)
    {
        // Only the customer's own orders in this session; another customer's visit is not found.
        var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Session)
            .Where(o => o.SessionId == id && o.CustomerId == customerId)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
        if (orders.Count == 0 || orders[0].Session is null)
        {
            return NotFound();
        }

        Visit = new DiningVisit(orders[0].Session!, orders);

        var orderIds = orders.Select(o => o.Id).ToList();
        OrderItems = (await _db.OrderItems
                .AsNoTracking()
                .Where(i => orderIds.Contains(i.OrderId))
                .ToListAsync())
            .ToLookup(i => i.OrderId);
        Currency = await _db.RestaurantSettings.AsNoTracking().Select(s => (CurrencyType?)s.DefaultCurrency).FirstOrDefaultAsync()
            ?? CurrencyType.MYR;

        await LoadPaymentsAsync(orderIds);
        return Page();
    }

    private async Task LoadPaymentsAsync(List<string> orderIds)
    {
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Order!.Customer)
            .Where(t => orderIds.Contains(t.OrderId)
                && (t.Status == TransactionStatus.Succeeded || t.Status == TransactionStatus.Refunded))
            .ToListAsync();

        var groups = transactions
            .GroupBy(t => t.StripeCheckoutSessionId ?? t.Id)
            .OrderBy(g => g.Min(t => t.CreatedAt))
            .ToList();
        var stripe = await Task.WhenAll(groups.Select(g => _stripe.GetAsync(
            g.First().StripeCheckoutSessionId, expectRefunded: g.Any(t => t.Status == TransactionStatus.Refunded))));

        Payments = groups.Select((group, index) =>
        {
            var first = group.First();
            var info = stripe[index];
            var promo = group.Sum(t => Metadata(t, "promoDiscount"));
            var depositUsed = group.Sum(t => Metadata(t, "reservationDepositCredit"));
            var promoCode = group.Select(t => MetadataText(t, "promotionCode")).FirstOrDefault(code => code is not null);

            var extra = new List<(string Icon, string Label, string Value)>();
            if (promo > 0)
            {
                extra.Add(("fa-solid fa-tag", promoCode is null ? "Promotion" : $"Promo {promoCode}", "-" + Currency.FormatCents(promo)));
            }
            if (depositUsed > 0)
            {
                extra.Add(("fa-solid fa-calendar-check", "Booking deposit used", "-" + Currency.FormatCents(depositUsed)));
            }

            return new PaymentPanel
            {
                AmountCents = group.Sum(t => t.Amount),
                CurrencySymbol = Currency.GetSymbol(),
                State = info?.Refunded == true || group.Any(t => t.Status == TransactionStatus.Refunded)
                    ? PaymentState.Refunded
                    : PaymentState.Paid,
                CheckoutSessionId = first.StripeCheckoutSessionId,
                Stripe = info,
                FallbackMethod = first.PaymentMethod.ToString(),
                FallbackName = first.Order?.Customer?.FullName,
                FallbackEmail = first.Order?.Customer?.Email,
                FallbackPaidAtUtc = first.CreatedAt,
                ExtraRows = extra
            };
        }).ToList();
    }

    // Numbers and text the payment page saved with each transaction (Pages/Payment).
    private static int Metadata(Transaction transaction, string name) =>
        Read(transaction, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;

    private static string? MetadataText(Transaction transaction, string name) =>
        Read(transaction, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? Read(Transaction transaction, string name)
    {
        if (string.IsNullOrWhiteSpace(transaction.MetadataJson))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(transaction.MetadataJson);
            return json.RootElement.TryGetProperty(name, out var value) ? value.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
