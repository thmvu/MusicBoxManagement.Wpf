using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Printing
{
    public static class InvoiceDocument
    {
        public static readonly Size A4 = new Size(793.7, 1122.5);
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        public static FlowDocument Create(InvoiceDetails details, Size pageSize)
        {
            if (details == null) throw new ArgumentNullException(nameof(details));
            if (double.IsNaN(pageSize.Width) || double.IsInfinity(pageSize.Width) || pageSize.Width < 480
                || double.IsNaN(pageSize.Height) || double.IsInfinity(pageSize.Height) || pageSize.Height < 480)
                throw new InvalidOperationException("Vùng in quá nhỏ. Hãy chọn khổ A4 hoặc khổ giấy lớn hơn.");
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = Brushes.Black, Background = Brushes.White, PageWidth = pageSize.Width,
                PageHeight = pageSize.Height, PagePadding = new Thickness(32), ColumnWidth = pageSize.Width,
                ColumnGap = 0, Language = System.Windows.Markup.XmlLanguage.GetLanguage("vi-VN") };
            doc.Blocks.Add(Text(details.StoreName, 24, true));
            doc.Blocks.Add(Text("HÓA ĐƠN ĐÃ THANH TOÁN", 16, true));
            doc.Blocks.Add(Text("Mã hóa đơn: " + details.Invoice.InvoiceNumber));
            doc.Blocks.Add(Text("Phòng: " + details.RoomCodeSnapshot + " — " + details.RoomTypeNameSnapshot
                + " (" + details.RoomTypeCodeSnapshot + ")"));
            doc.Blocks.Add(Text("Nhận phòng: " + Local(details.ActualStartTime) + "\nTrả phòng: " + Local(details.ActualEndTime)));
            doc.Blocks.Add(Text("Thời lượng: " + details.UsedMinutes.ToString("N2", Vietnamese) + " phút"
                + "\nĐơn giá phòng: " + Money(details.HourlyRate) + "/giờ"));
            doc.Blocks.Add(Text("Món đã phục vụ", 14, true));
            if (details.Items.Count == 0) doc.Blocks.Add(Text("Không có món đã phục vụ."));
            else
            {
                var table = new Table { CellSpacing = 0, FontSize = 10, Margin = new Thickness(0, 0, 0, 12) };
                foreach (var proportion in new[] { .36, .07, .27, .30 })
                    table.Columns.Add(new TableColumn { Width = new GridLength((pageSize.Width - 64) * proportion) });
                var rows = new TableRowGroup(); table.RowGroups.Add(rows);
                rows.Rows.Add(Row(true, "Tên món", "SL", "Đơn giá (đ)", "Thành tiền (đ)"));
                foreach (var item in details.Items)
                    rows.Rows.Add(Row(false, item.ServiceNameSnapshot, item.Quantity.ToString(Vietnamese),
                        item.UnitPrice.ToString("N0", Vietnamese), item.Amount.ToString("N0", Vietnamese)));
                doc.Blocks.Add(table);
            }
            // Keep the settled totals together; never recalculate a historical invoice here.
            var totals = new Section();
            totals.Blocks.Add(Text("Tiền phòng: " + Money(details.Invoice.RoomCharge), 12, false, true));
            totals.Blocks.Add(Text("Tiền dịch vụ: " + Money(details.Invoice.ServiceCharge), 12, false, true));
            totals.Blocks.Add(Text("Tổng đã thanh toán: " + Money(details.Invoice.TotalAmount), 16, true, true));
            totals.Blocks.Add(Text("Hình thức: " + (details.Invoice.PaymentMethod == "Cash" ? "Tiền mặt" : "Chuyển khoản"), 12, false, true));
            totals.Blocks.Add(Text("Thu ngân: " + details.Invoice.ProcessedByNameSnapshot, 12, false, true));
            totals.Blocks.Add(Text("Thanh toán: " + Local(details.Invoice.PaidAt)));
            doc.Blocks.Add(totals);
            ((IDocumentPaginatorSource)doc).DocumentPaginator.PageSize = pageSize;
            return doc;
        }
        private static Paragraph Text(string value, double size = 12, bool bold = false, bool next = false)
            => new Paragraph(new Run(value)) { FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 8), KeepTogether = true, KeepWithNext = next };
        private static TableRow Row(bool heading, params string[] values)
        {
            var row = new TableRow { Background = heading ? Brushes.LightGray : Brushes.White };
            for (var i = 0; i < values.Length; i++)
            {
                var text = Text(values[i], 10, heading); text.Margin = new Thickness(0);
                text.TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right;
                row.Cells.Add(new TableCell(text) { Padding = new Thickness(4, 6, 4, 6),
                    BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            return row;
        }
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", Vietnamese);
        private static string Money(decimal value) => value.ToString("N0", Vietnamese) + " đ";
    }
}
