using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RestaurantReservation.Extensions;

namespace RestaurantReservation.Infrastructure.Admin;

// Writes the analytics report as one CSV file, with a titled section per table separated by blank rows.
// Amounts are plain numbers (e.g. 1234.50) so spreadsheets can add them up; the currency is in the column name.
public static partial class AnalyticsReportCsv
{
    public static byte[] Write(AnalyticsReport report)
    {
        var csv = new StringBuilder();
        var code = report.Currency.GetCode();
        string Money(int cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Time(DateTime time) => time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        void Row(params object?[] cells) => csv.AppendLine(string.Join(",", cells.Select(c => Escape(c?.ToString()))));
        void Section(string title)
        {
            if (csv.Length > 0)
            {
                csv.AppendLine();
            }
            Row(title);
        }

        Section("Business analytics report");
        Row("Restaurant", report.RestaurantName);
        Row("Period", $"{Date(report.From)} to {Date(report.To)}", $"{report.DayCount} day(s)");
        Row("Compared with", $"{Date(report.PreviousFrom)} to {Date(report.PreviousTo)}");
        Row("Generated", Time(report.GeneratedAt));
        Row("Currency", code);

        Section("Summary");
        Row("Metric", "This period", "Previous period", "Change");
        void Metric(string name, decimal current, decimal previous, Func<decimal, string> format, bool isRate = false) =>
            Row(name, format(current), format(previous), isRate
                ? AnalyticsReport.ChangeInPoints(current, previous)
                : AnalyticsReport.Change(current, previous));
        var (now, before) = (report.Current, report.Previous);
        Metric($"Revenue ({code})", now.Revenue, before.Revenue, v => Money((int)v));
        Metric("Orders placed", now.OrdersPlaced, before.OrdersPlaced, Number);
        Metric("Orders served", now.OrdersServed, before.OrdersServed, Number);
        Metric("Orders cancelled", now.OrdersCancelled, before.OrdersCancelled, Number);
        Metric($"Average order value ({code})", now.AverageOrderValue, before.AverageOrderValue, v => Money((int)v));
        Metric("Items sold", now.ItemsSold, before.ItemsSold, Number);
        Metric("Reservations", now.Reservations, before.Reservations, Number);
        Metric("Reserved guests", now.Guests, before.Guests, Number);
        Metric("Reservation cancellation rate, no-shows included (%)", now.CancellationRate, before.CancellationRate, Number, isRate: true);
        Metric("No-shows (counted as cancelled)", now.NoShows, before.NoShows, Number);
        Row("Average booking lead time (days)", now.AverageLeadDays?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
            before.AverageLeadDays?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
            now.AverageLeadDays is { } lead && before.AverageLeadDays is { } leadBefore
                ? AnalyticsReport.Change((decimal)lead, (decimal)leadBefore)
                : "");
        Metric("Table utilization (%)", now.UtilizationPercent, before.UtilizationPercent, Number, isRate: true);

        Section("Daily breakdown");
        Row("Date", "Day", "Orders placed", "Orders served", $"Revenue ({code})", $"Average order value ({code})", "Reservations", "Reserved guests");
        foreach (var day in report.Days)
        {
            Row(Date(day.Date), day.Date.DayOfWeek, day.OrdersPlaced, day.OrdersServed, Money(day.Revenue),
                Money(day.AverageOrderValue), day.Reservations, day.Guests);
        }
        Row("Total", "", report.Days.Sum(d => d.OrdersPlaced), report.Days.Sum(d => d.OrdersServed),
            Money(report.Days.Sum(d => d.Revenue)), Money(now.AverageOrderValue),
            report.Days.Sum(d => d.Reservations), report.Days.Sum(d => d.Guests));

        Section("Orders by hour of day");
        Row("Hour", "Orders placed", $"Revenue from orders served ({code})");
        foreach (var hour in report.Hours)
        {
            Row(hour.Label, hour.OrdersPlaced, Money(hour.Revenue));
        }

        Section("Menu item performance (served and completed orders)");
        Row("Rank", "Item", "Category", "Units sold", $"Revenue ({code})", "Share of item revenue (%)");
        var rank = 1;
        foreach (var item in report.Items)
        {
            Row(rank++, item.Name, item.Category, item.Units, Money(item.Revenue), Number(item.ShareOfRevenue));
        }

        Section("Sales by category");
        Row("Category", "Units sold", $"Revenue ({code})", "Share of item revenue (%)");
        foreach (var category in report.Categories)
        {
            Row(category.Category, category.Units, Money(category.Revenue), Number(category.ShareOfRevenue));
        }

        Section("Payments collected");
        Row("Payment method", "Payments", $"Amount ({code})");
        foreach (var payment in report.Payments)
        {
            Row(payment.Method, payment.Payments, Money(payment.Amount));
        }
        Row("Total collected", report.Payments.Sum(p => p.Payments), Money(report.Payments.Sum(p => p.Amount)));
        Row($"Promo code discounts ({code})", "", Money(report.PromoDiscounts));
        Row($"Reservation deposits used ({code})", "", Money(report.DepositCredits));

        Section("Reservations by status");
        Row("Status", "Reservations", "Guests");
        foreach (var status in report.ReservationStatuses)
        {
            Row(status.Status, status.Reservations, status.Guests);
        }

        var utilization = report.Utilization;
        Section("Table utilization (table-hours in use out of table-hours open)");
        Row("Table", "Seats", "Visits started", "Hours in use", "Utilization (%)");
        foreach (var table in utilization.Tables)
        {
            Row(table.TableId, table.Capacity, table.Visits, Number(Math.Round((decimal)table.UsedHours, 1)),
                Number(Math.Round((decimal)table.Rate * 100, 1)));
        }
        Row("All tables", utilization.Tables.Sum(t => t.Capacity), utilization.Visits,
            Number(Math.Round((decimal)utilization.UsedHours, 1)), Number(now.UtilizationPercent));
        Row("Table-hours open", Number(Math.Round((decimal)utilization.OpenTableHours, 1)));
        Row("Average visit (minutes)", utilization.AverageVisitMinutes);

        Section("Reservations by time slot (no-shows and cancellations left out)");
        Row("Time slot", "Bookings", "Guests");
        foreach (var slot in report.ReservationSlots)
        {
            Row(slot.Time.ToString("HH:mm", CultureInfo.InvariantCulture), slot.Bookings, slot.Guests);
        }

        Section("Peak dining hours (average tables in use, from dining sessions)");
        var peakRows = report.PeakHours.Rows;
        if (peakRows.Count > 0)
        {
            Row(new object?[] { "Day" }.Concat(peakRows[0].Hours.Select(h => (object?)$"{h.Hour:00}:00")).ToArray());
            foreach (var peakRow in peakRows)
            {
                Row(new object?[] { peakRow.Day }.Concat(peakRow.Hours.Select(h =>
                    (object?)(h.AverageTables is { } tables ? Number(Math.Round((decimal)tables, 2)) : ""))).ToArray());
            }
        }

        Section("Orders");
        Row("Order", "Placed", "Served", "Table", "Customer", "Items", "Status", $"Total ({code})");
        foreach (var order in report.Orders)
        {
            Row(order.Id, Time(order.PlacedAt), order.ServedAt is { } served ? Time(served) : "", order.Table,
                order.Customer ?? "Guest", order.Items, order.Status, Money(order.Total));
        }

        // The byte order mark makes Excel read the file as UTF-8 (names with accents, the € sign).
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    // Quotes a cell when it holds a comma, quote or line break. A text cell starting with =, +, - or @ gets a
    // leading apostrophe so a spreadsheet shows it as text instead of running it as a formula (e.g. a dish name).
    // Signed numbers such as "-3.5", "+12.5%" or "+4.0 pts" are left alone.
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !SignedNumber().IsMatch(value))
        {
            value = "'" + value;
        }
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    [GeneratedRegex(@"^[+-]\d+(\.\d+)?( pts|%)?$")]
    private static partial Regex SignedNumber();
}
