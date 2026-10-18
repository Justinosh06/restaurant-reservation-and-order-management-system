using RestaurantReservation.Models;

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
    int? DepositAmount = null)
{
    // "RM20 deposit paid", or null when the booking has no deposit.
    public string? DepositLabel => Deposit switch
    {
        DepositStatus.Paid => $"RM{DepositAmount / 100m:0.##} deposit paid",
        DepositStatus.Refunded => $"RM{DepositAmount / 100m:0.##} deposit refunded",
        _ => null
    };

    // How long a table is held for one booking.
    public static readonly TimeSpan SlotLength = TimeSpan.FromHours(2);

    public DateTime EndsAt => ReservationAt + SlotLength;

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

    public static ReservationSummary From(Reservation reservation) =>
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
            reservation.DepositAmount);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
