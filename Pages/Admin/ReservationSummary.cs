using System.Globalization;
using RestaurantReservation.Extensions;
using RestaurantReservation.Models;
using RestaurantReservation.Models.Enums;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

// A reservation as the admin pages show it. Guest details come from the booking itself
// (taken by staff) or, when it has none, from the customer's account.
public sealed record ReservationSummary(
    string Id,
    IReadOnlyList<string> TableIds,
    string CustomerName,
    string? ContactNumber,
    int PartySize,
    DateTime ReservationAt,
    string? Notes,
    ReservationStatus Status,
    DepositStatus Deposit = DepositStatus.None,
    int? DepositAmount = null,
    DateTime? CheckedInAt = null,
    CurrencyType? DepositCurrency = null)
{
    // "RM20 deposit paid" in the currency it was paid in, or null when the booking has no deposit.
    public string? DepositLabel => Deposit switch
    {
        DepositStatus.Paid => $"{DepositText(DepositAmount, DepositCurrency)} deposit paid",
        DepositStatus.Refunded => $"{DepositText(DepositAmount, DepositCurrency)} deposit refunded",
        _ => null
    };

    // "RM20" or "$20.50". Deposits saved before their currency was kept were all in ringgit.
    public static string DepositText(int? amountCents, CurrencyType? currency)
    {
        var amount = (amountCents ?? 0) / 100m;
        return $"{(currency ?? CurrencyType.MYR).GetSymbol()}{amount.ToString(amount % 1 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture)}";
    }

    // How long a table is held for one booking.
    public static readonly TimeSpan SlotLength = TimeSpan.FromHours(2);

    public DateTime EndsAt => ReservationAt + SlotLength;

    // Guests who are checked in keep their table until staff check them out, even past the slot.
    public bool IsSeated => Status == ReservationStatus.CheckedIn;

    public bool IsActive(DateTime now) => IsSeated || EndsAt > now;

    public bool HasTable => TableIds.Count > 0;

    // "T3" or "T3 + T4" for a party spread over several tables.
    public string TableLabel => string.Join(" + ", TableIds);

    public bool Overlaps(DateTime start) => ReservationAt < start + SlotLength && start < EndsAt;

    // The booking already holding tableId around start, if any. ignoreId is the reservation being placed.
    public static ReservationSummary? FindClash(
        IEnumerable<ReservationSummary> reservations, string tableId, DateTime start, string? ignoreId) =>
        reservations.FirstOrDefault(item => item.Id != ignoreId
            && item.TableIds.Contains(tableId)
            && item.Status != ReservationStatus.Cancelled
            && item.Overlaps(start));

    public static ReservationSummary From(RestaurantReservation.Models.Reservation reservation) =>
        new(
            reservation.Id,
            reservation.ReservationTables
                .Select(link => link.TableId)
                .OrderBy(id => id.Length)
                .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FirstNonEmpty(reservation.GuestName, reservation.Customer?.FullName) ?? "Guest",
            FirstNonEmpty(reservation.ContactNumber, reservation.Customer?.PhoneNumber),
            reservation.Pax,
            reservation.Date.ToDateTime(reservation.TimeSlot),
            reservation.Notes,
            reservation.Status,
            reservation.DepositStatus,
            reservation.DepositAmount,
            reservation.CheckedInAt,
            reservation.DepositCurrency);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
