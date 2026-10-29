using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RestaurantReservation.Extensions;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Infrastructure.Payments;

public sealed record CashReceiptData(
    string RestaurantName,
    string RestaurantAddress,
    string ReceiptNumber,
    string TableId,
    string Customer,
    string CustomerEmail,
    CurrencyType Currency,
    DateTime PaidAtUtc,
    int CashTendered,
    int ChangeDue,
    IReadOnlyList<CashReceiptOrder> Orders)
{
    public int TotalPaid => Orders.Sum(order => order.AmountPaid);
    public int TotalOrderValue => Orders.Sum(order => order.OrderTotal);
    public int TotalPromoDiscount => Orders.Sum(order => order.PromoDiscount);
    public int TotalDepositCredit => Orders.Sum(order => order.ReservationDepositCredit);
}

public sealed record CashReceiptOrder(
    string OrderId,
    DateTime CreatedAtUtc,
    int OrderTotal,
    int PromoDiscount,
    int ReservationDepositCredit,
    string? PromotionCode,
    int AmountPaid,
    IReadOnlyList<CashReceiptItem> Items);

public sealed record CashReceiptItem(string Name, int Quantity, int UnitPrice);

public static class CashReceiptPdf
{
    public static byte[] Write(CashReceiptData receipt) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().PaddingBottom(14).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                .Column(header =>
                {
                    header.Item().Text(receipt.RestaurantName).FontSize(20).SemiBold();
                    if (!string.IsNullOrWhiteSpace(receipt.RestaurantAddress))
                    {
                        header.Item().Text(receipt.RestaurantAddress).FontColor(Colors.Grey.Darken1);
                    }
                });

            page.Content().PaddingTop(20).Column(content =>
            {
                content.Spacing(16);
                content.Item().Text("Cash payment receipt").FontSize(16).SemiBold();
                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    AddDetail(table, "Receipt", receipt.ReceiptNumber);
                    AddDetail(table, "Table", receipt.TableId);
                    AddDetail(table, "Customer", receipt.Customer);
                    AddDetail(table, "Email", receipt.CustomerEmail);
                    AddDetail(table, "Paid", receipt.PaidAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.CurrentCulture));
                    AddDetail(table, "Payment method", "Cash");
                });

                foreach (var order in receipt.Orders)
                {
                    content.Item().Column(orderColumn =>
                    {
                        orderColumn.Spacing(6);
                        orderColumn.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Order {order.OrderId}").SemiBold();
                            row.ConstantItem(170).AlignRight()
                                .Text($"Paid: {Money(receipt.Currency, order.AmountPaid)}").SemiBold();
                        });
                        orderColumn.Item().Text(
                            $"Placed {order.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.CurrentCulture)}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        orderColumn.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                HeaderCell(header.Cell(), "Item");
                                HeaderCell(header.Cell(), "Qty");
                                HeaderCell(header.Cell(), "Unit price");
                                HeaderCell(header.Cell(), "Total");
                            });

                            foreach (var item in order.Items)
                            {
                                BodyCell(table.Cell(), item.Name);
                                BodyCell(table.Cell(), item.Quantity.ToString(CultureInfo.CurrentCulture));
                                BodyCell(table.Cell(), Money(receipt.Currency, item.UnitPrice));
                                BodyCell(table.Cell(), Money(receipt.Currency, item.UnitPrice * item.Quantity));
                            }
                        });
                        orderColumn.Item().AlignRight().Column(totals =>
                        {
                            totals.Item().Text($"Order total: {Money(receipt.Currency, order.OrderTotal)}");
                            if (order.PromoDiscount > 0)
                            {
                                var promotion = string.IsNullOrWhiteSpace(order.PromotionCode)
                                    ? "Promotion"
                                    : $"Promo {order.PromotionCode}";
                                totals.Item().Text($"{promotion}: -{Money(receipt.Currency, order.PromoDiscount)}");
                            }
                            if (order.ReservationDepositCredit > 0)
                            {
                                totals.Item().Text($"Reservation deposit: -{Money(receipt.Currency, order.ReservationDepositCredit)}");
                            }
                            totals.Item().Text($"Amount paid: {Money(receipt.Currency, order.AmountPaid)}").SemiBold();
                        });
                    });
                }

                content.Item().PaddingTop(8).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Column(total =>
                {
                    total.Item().AlignRight().Text($"Orders total: {Money(receipt.Currency, receipt.TotalOrderValue)}");
                    if (receipt.TotalPromoDiscount > 0)
                    {
                        total.Item().AlignRight().Text($"Promotions: -{Money(receipt.Currency, receipt.TotalPromoDiscount)}");
                    }
                    if (receipt.TotalDepositCredit > 0)
                    {
                        total.Item().AlignRight().Text($"Reservation deposits: -{Money(receipt.Currency, receipt.TotalDepositCredit)}");
                    }
                    total.Item().AlignRight().Text($"Total cash received: {Money(receipt.Currency, receipt.TotalPaid)}")
                        .FontSize(13).SemiBold();
                    total.Item().AlignRight().Text($"Cash tendered: {Money(receipt.Currency, receipt.CashTendered)}");
                    total.Item().AlignRight().Text($"Change returned: {Money(receipt.Currency, receipt.ChangeDue)}").SemiBold();
                });
            });

            page.Footer().PaddingTop(10).AlignCenter()
                .Text("Thank you for dining with us.").FontSize(9).FontColor(Colors.Grey.Darken1);
        })).GeneratePdf();

    private static void AddDetail(TableDescriptor table, string label, string value)
    {
        BodyCell(table.Cell(), label);
        BodyCell(table.Cell(), value);
    }

    private static void HeaderCell(IContainer container, string text) =>
        container.Background(Colors.Grey.Lighten3).Padding(6).Text(text).SemiBold();

    private static void BodyCell(IContainer container, string text) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(text);

    private static string Money(CurrencyType currency, int cents) =>
        $"{currency.GetSymbol()} {cents / 100m:N2}";
}
