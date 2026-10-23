using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RestaurantReservation.Infrastructure.Admin;

// Lays out the analytics report as an A4 PDF: headline numbers compared with the previous period, a daily
// revenue chart, then the tables. The full order list is left to the CSV, where it can be filtered.
public static class AnalyticsReportPdf
{
    private const int TopItemCount = 15;

    private static readonly string Ink = Colors.Grey.Darken4;
    private static readonly string Muted = Colors.Grey.Darken1;
    private static readonly string Rule = Colors.Grey.Lighten2;
    private static readonly string Panel = Colors.Grey.Lighten4;
    private static readonly string Bar = Colors.Grey.Darken3;
    private static readonly string Up = Colors.Green.Darken2;
    private static readonly string Down = Colors.Red.Darken2;

    public static byte[] Write(AnalyticsReport report) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(style => style.FontSize(9).FontColor(Ink));

            page.Header().Element(header => Header(header, report));
            page.Content().PaddingTop(16).Column(content =>
            {
                content.Spacing(20);
                content.Item().Element(c => Summary(c, report));
                content.Item().Element(c => RevenueChart(c, report));
                content.Item().Element(c => DailyTable(c, report));
                content.Item().Element(c => HourTable(c, report));
                content.Item().Element(c => ItemTables(c, report));
                content.Item().Element(c => PaymentAndReservationTables(c, report));
            });
            page.Footer().PaddingTop(8).Row(footer =>
            {
                footer.RelativeItem().Text($"Generated {report.GeneratedAt:d MMM yyyy, HH:mm}").FontSize(8).FontColor(Muted);
                footer.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        })).GeneratePdf();

    private static void Header(IContainer container, AnalyticsReport report)
    {
        container.BorderBottom(1).BorderColor(Rule).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(report.RestaurantName).FontSize(16).SemiBold();
                if (!string.IsNullOrWhiteSpace(report.RestaurantAddress))
                {
                    column.Item().Text(report.RestaurantAddress).FontColor(Muted);
                }
            });
            row.RelativeItem().AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text("Business analytics report").FontSize(12).SemiBold();
                column.Item().AlignRight().Text(DateRange(report.From, report.To)).FontColor(Muted);
                column.Item().AlignRight().Text($"Compared with {DateRange(report.PreviousFrom, report.PreviousTo)}")
                    .FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void Summary(IContainer container, AnalyticsReport report)
    {
        var (now, before) = (report.Current, report.Previous);
        var cards = new (string Label, string Value, decimal Current, decimal Previous, bool LowerIsBetter, bool IsRate)[]
        {
            ("Revenue", report.Money(now.Revenue), now.Revenue, before.Revenue, false, false),
            ("Orders placed", now.OrdersPlaced.ToString("N0"), now.OrdersPlaced, before.OrdersPlaced, false, false),
            ("Avg. order value", report.Money(now.AverageOrderValue), now.AverageOrderValue, before.AverageOrderValue, false, false),
            ("Items sold", now.ItemsSold.ToString("N0"), now.ItemsSold, before.ItemsSold, false, false),
            ("Reservations", now.Reservations.ToString("N0"), now.Reservations, before.Reservations, false, false),
            ("Reserved guests", now.Guests.ToString("N0"), now.Guests, before.Guests, false, false),
            ("Orders cancelled", now.OrdersCancelled.ToString("N0"), now.OrdersCancelled, before.OrdersCancelled, true, false),
            ("Booking cancellation rate", $"{now.CancellationRate:0.#}%", now.CancellationRate, before.CancellationRate, true, true)
        };

        container.Column(column =>
        {
            column.Spacing(8);
            foreach (var cardRow in cards.Chunk(4))
            {
                column.Item().Row(row =>
                {
                    row.Spacing(8);
                    foreach (var card in cardRow)
                    {
                        row.RelativeItem().Background(Panel).Padding(10).Column(cell =>
                        {
                            cell.Item().Text(card.Label).FontSize(8).FontColor(Muted);
                            cell.Item().Text(card.Value).FontSize(14).SemiBold();
                            var change = card.IsRate
                                ? AnalyticsReport.ChangeInPoints(card.Current, card.Previous)
                                : AnalyticsReport.Change(card.Current, card.Previous);
                            var better = card.Current == card.Previous ? (bool?)null : (card.Current > card.Previous) != card.LowerIsBetter;
                            cell.Item().Text(text =>
                            {
                                text.Span(change).FontSize(8).SemiBold()
                                    .FontColor(better is null ? Muted : better.Value ? Up : Down);
                                text.Span(" vs previous").FontSize(8).FontColor(Muted);
                            });
                        });
                    }
                });
            }
        });
    }

    // One bar per day, scaled to the best day. A single day is shown by hour instead.
    private static void RevenueChart(IContainer container, AnalyticsReport report)
    {
        var oneDay = report.Days.Count == 1;
        // Every hour from the first to the last busy one, so quiet hours show as gaps instead of disappearing.
        var bars = oneDay
            ? (report.Hours.Count == 0 ? [] : Enumerable.Range(report.Hours[0].Hour, report.Hours[^1].Hour - report.Hours[0].Hour + 1))
                .Select(hour => (Label: new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt"),
                    Amount: report.Hours.FirstOrDefault(h => h.Hour == hour)?.Revenue ?? 0))
                .ToList()
            // Short dates once the bars get narrow, so the labels stay on one line.
            : report.Days.Select(d => (Label: d.Date.ToString(report.Days.Count > 14 ? "d/M" : "d MMM"), Amount: d.Revenue)).ToList();
        var best = bars.Count == 0 ? 0 : bars.Max(b => b.Amount);
        const float chartHeight = 110;

        container.Column(column =>
        {
            column.Item().Element(c => SectionTitle(c, oneDay ? "Revenue by hour" : "Revenue by day",
                best == 0 ? "No revenue in this period" : $"Best: {report.Money(best)}"));
            if (best == 0)
            {
                return;
            }

            column.Item().Height(chartHeight).BorderBottom(1).BorderColor(Rule).Row(row =>
            {
                row.Spacing(bars.Count > 45 ? 0.5f : 2);
                foreach (var bar in bars)
                {
                    var height = Math.Max(chartHeight * bar.Amount / best, bar.Amount > 0 ? 1.5f : 0.01f);
                    row.RelativeItem().AlignBottom().Height(height).Background(Bar);
                }
            });

            // A few date labels along the bottom: the first, the last and some in between.
            var step = Math.Max(1, (int)Math.Ceiling(bars.Count / 8.0));
            column.Item().PaddingTop(3).Row(row =>
            {
                for (var i = 0; i < bars.Count; i++)
                {
                    var label = i % step == 0 || i == bars.Count - 1 ? bars[i].Label : string.Empty;
                    row.RelativeItem().Text(label).FontSize(6).FontColor(Muted);
                }
            });
        });
    }

    private static void DailyTable(IContainer container, AnalyticsReport report)
    {
        container.Column(column =>
        {
            column.Item().Element(c => SectionTitle(c, "Daily breakdown"));
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2.2f);
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                HeaderRow(table, "Date", "Orders", "Served", "Revenue", "Avg. order", "Bookings", "Guests");
                foreach (var day in report.Days)
                {
                    BodyRow(table, day.Date.ToString("ddd, d MMM yyyy"), day.OrdersPlaced.ToString("N0"), day.OrdersServed.ToString("N0"),
                        report.Money(day.Revenue), report.Money(day.AverageOrderValue), day.Reservations.ToString("N0"), day.Guests.ToString("N0"));
                }
                TotalRow(table, "Total", report.Current.OrdersPlaced.ToString("N0"), report.Current.OrdersServed.ToString("N0"),
                    report.Money(report.Current.Revenue), report.Money(report.Current.AverageOrderValue),
                    report.Current.Reservations.ToString("N0"), report.Current.Guests.ToString("N0"));
            });
        });
    }

    private static void HourTable(IContainer container, AnalyticsReport report)
    {
        container.ShowEntire().Column(column =>
        {
            column.Item().Element(c => SectionTitle(c, "Busiest hours", "When orders were placed"));
            if (report.Hours.Count == 0)
            {
                column.Item().Text("No orders in this period.").FontColor(Muted);
                return;
            }

            var busiest = report.Hours.Max(h => h.OrdersPlaced);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(4);
                    c.RelativeColumn();
                    c.RelativeColumn(1.4f);
                });
                HeaderRow(table, "Hour", "", "Orders", "Revenue served");
                foreach (var hour in report.Hours)
                {
                    table.Cell().Element(BodyCell).Text(hour.Label);
                    table.Cell().Element(BodyCell).AlignMiddle().Element(c => ShareBar(c, hour.OrdersPlaced, busiest));
                    table.Cell().Element(BodyCell).AlignRight().Text(hour.OrdersPlaced.ToString("N0"));
                    table.Cell().Element(BodyCell).AlignRight().Text(report.Money(hour.Revenue));
                }
            });
        });
    }

    private static void ItemTables(IContainer container, AnalyticsReport report)
    {
        container.Column(column =>
        {
            column.Spacing(20);
            column.Item().Column(items =>
            {
                var shown = report.Items.Take(TopItemCount).ToList();
                items.Item().Element(c => SectionTitle(c, "Top menu items",
                    report.Items.Count > TopItemCount ? $"Top {TopItemCount} of {report.Items.Count} by revenue (all are in the CSV)" : "By revenue"));
                if (shown.Count == 0)
                {
                    items.Item().Text("No items sold in this period.").FontColor(Muted);
                    return;
                }

                var bestShare = shown.Max(i => i.ShareOfRevenue);
                items.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(18);
                        c.RelativeColumn(3);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(0.8f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(2);
                    });
                    HeaderRow(table, 3, "#", "Item", "Category", "Units", "Revenue", "Share");
                    var rank = 1;
                    foreach (var item in shown)
                    {
                        table.Cell().Element(BodyCell).Text((rank++).ToString()).FontColor(Muted);
                        table.Cell().Element(BodyCell).Text(item.Name);
                        table.Cell().Element(BodyCell).Text(item.Category).FontColor(Muted);
                        table.Cell().Element(BodyCell).AlignRight().Text(item.Units.ToString("N0"));
                        table.Cell().Element(BodyCell).AlignRight().Text(report.Money(item.Revenue));
                        table.Cell().Element(BodyCell).PaddingLeft(8).Row(row =>
                        {
                            row.RelativeItem().AlignMiddle().Element(c => ShareBar(c, item.ShareOfRevenue, bestShare));
                            row.ConstantItem(30).AlignRight().Text($"{item.ShareOfRevenue:0.#}%");
                        });
                    }
                });
            });

            if (report.Categories.Count > 0)
            {
                column.Item().ShowEntire().Column(categories =>
                {
                    categories.Item().Element(c => SectionTitle(c, "Sales by category"));
                    categories.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                            c.RelativeColumn(1.4f);
                            c.RelativeColumn();
                        });
                        HeaderRow(table, "Category", "Units", "Revenue", "Share");
                        foreach (var category in report.Categories)
                        {
                            BodyRow(table, category.Category, category.Units.ToString("N0"), report.Money(category.Revenue),
                                $"{category.ShareOfRevenue:0.#}%");
                        }
                    });
                });
            }
        });
    }

    private static void PaymentAndReservationTables(IContainer container, AnalyticsReport report)
    {
        container.ShowEntire().Row(row =>
        {
            row.Spacing(20);
            row.RelativeItem().Column(column =>
            {
                column.Item().Element(c => SectionTitle(c, "Payments collected"));
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn();
                        c.RelativeColumn(1.4f);
                    });
                    HeaderRow(table, "Method", "Payments", "Amount");
                    foreach (var payment in report.Payments)
                    {
                        BodyRow(table, payment.Method, payment.Payments.ToString("N0"), report.Money(payment.Amount));
                    }
                    TotalRow(table, "Total collected", report.Payments.Sum(p => p.Payments).ToString("N0"),
                        report.Money(report.Payments.Sum(p => p.Amount)));
                });
                column.Item().PaddingTop(6).Text($"Promo code discounts: {report.Money(report.PromoDiscounts)}").FontColor(Muted);
                column.Item().Text($"Reservation deposits used: {report.Money(report.DepositCredits)}").FontColor(Muted);
            });

            row.RelativeItem().Column(column =>
            {
                column.Item().Element(c => SectionTitle(c, "Reservations by status"));
                if (report.ReservationStatuses.Count == 0)
                {
                    column.Item().Text("No reservations for these dates.").FontColor(Muted);
                    return;
                }
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });
                    HeaderRow(table, "Status", "Bookings", "Guests");
                    foreach (var status in report.ReservationStatuses)
                    {
                        BodyRow(table, status.Status, status.Reservations.ToString("N0"), status.Guests.ToString("N0"));
                    }
                });
            });
        });
    }

    private static void SectionTitle(IContainer container, string title, string? note = null)
    {
        container.PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Text(title).FontSize(11).SemiBold();
            if (note is not null)
            {
                row.AutoItem().AlignBottom().Text(note).FontSize(8).FontColor(Muted);
            }
        });
    }

    private static void ShareBar(IContainer container, decimal value, decimal max)
    {
        var filled = max <= 0 ? 0 : (float)(value / max);
        container.Height(6).Row(row =>
        {
            if (filled > 0)
            {
                row.RelativeItem(filled).Background(Bar);
            }
            if (filled < 1)
            {
                row.RelativeItem(1 - filled).Background(Panel);
            }
        });
    }

    // The first column is left-aligned text; every other column holds numbers, aligned right.
    private static void HeaderRow(TableDescriptor table, params string[] cells) => HeaderRow(table, 1, cells);

    // The first textColumns columns are left-aligned text; the rest hold numbers, aligned right.
    private static void HeaderRow(TableDescriptor table, int textColumns, params string[] cells)
    {
        table.Header(header =>
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = header.Cell().BorderBottom(1).BorderColor(Ink).PaddingVertical(4);
                (i < textColumns ? cell : cell.AlignRight()).Text(cells[i]).FontSize(8).SemiBold().FontColor(Muted);
            }
        });
    }

    private static void BodyRow(TableDescriptor table, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = table.Cell().Element(BodyCell);
            (i == 0 ? cell : cell.AlignRight()).Text(cells[i]);
        }
    }

    private static void TotalRow(TableDescriptor table, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = table.Cell().BorderTop(1).BorderColor(Ink).PaddingVertical(4);
            (i == 0 ? cell : cell.AlignRight()).Text(cells[i]).SemiBold();
        }
    }

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(4);

    private static string DateRange(DateOnly from, DateOnly to) =>
        from == to ? from.ToString("d MMM yyyy") : $"{from:d MMM yyyy} - {to:d MMM yyyy}";
}
