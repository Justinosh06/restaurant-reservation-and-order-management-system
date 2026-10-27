using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
using RestaurantReservation.Services;
using Stripe;

namespace RestaurantReservation.Infrastructure.Reservations;

// A customer cancelling their own booking, from the booking page (Pages/Customer/Reserve) or the Reservations
// page (Pages/Customer/Reservations). Both use the same rule and the same steps.
public static class ReservationCancellation
{
    // Online cancelling closes this long before the booking starts; after that the customer has to call.
    public static readonly TimeSpan Notice = TimeSpan.FromHours(24);

    public static bool CanCancel(DateTime reservationAt, DateTime now) => reservationAt - now >= Notice;

    public static bool CanCancel(Reservation reservation, DateTime now) =>
        reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed
        && CanCancel(reservation.Date.ToDateTime(reservation.TimeSlot), now);

    // Refunds a paid deposit and marks the booking Cancelled. Returns the message to show the customer.
    public static async Task<string> CancelAsync(
        ApplicationDbContext db, IPaymentService payments, ReservationEmailQueue emails, ILogger logger,
        string? customerId, string? reservationId)
    {
        var reservation = reservationId is null ? null : await db.Reservations
            .FirstOrDefaultAsync(item => item.Id == reservationId && item.CustomerId == customerId);
        if (reservation is null || reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            return "That booking can no longer be cancelled.";
        }

        var at = reservation.Date.ToDateTime(reservation.TimeSlot)
            .ToString("dddd d MMMM 'at' h:mm tt", CultureInfo.CurrentCulture);
        if (!CanCancel(reservation, DateTime.Now))
        {
            return $"Your booking on {at} starts in less than {Notice.TotalHours:0} hours, so it can't be cancelled online. "
                + "Please call the restaurant.";
        }

        // The deposit is refunded in full; if Stripe refuses, the booking stays so nothing is lost.
        var refunded = false;
        if (reservation.DepositStatus == DepositStatus.Paid && reservation.StripeSessionId is not null)
        {
            try
            {
                await payments.RefundCheckoutSessionAsync(reservation.StripeSessionId);
                reservation.DepositStatus = DepositStatus.Refunded;
                refunded = true;
            }
            catch (Exception error) when (error is StripeException or InvalidOperationException)
            {
                logger.LogError(error, "Could not refund the deposit for reservation {ReservationId}.", reservation.Id);
                return "We couldn't refund your deposit just now, so your booking is still on. Please try again or call us.";
            }
        }

        // Kept as Cancelled (not deleted) so staff can still see it; cancelled bookings don't hold tables.
        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();
        emails.Cancelled(reservation, refunded);
        return $"Your booking on {at} is cancelled."
            + (refunded
                ? $" Your RM{reservation.DepositAmount / 100m:0.##} deposit is on its way back to your card (usually 5-10 working days)."
                : "");
    }
}
