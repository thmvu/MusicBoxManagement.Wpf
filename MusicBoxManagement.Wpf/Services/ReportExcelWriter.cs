using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Shared Open XML reports, written with framework libraries; no Excel installation.
    // No formulas/macros/external links. Untrusted labels are inline text cells.
    internal static class ReportExcelWriter
    {
        private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        internal static ReportExcelFile CreateRevenue(RevenueReport report, string mode)
        {
            var label = mode == "Day" ? "Theo ngày" : mode == "Month" ? "Theo tháng" : "Theo loại phòng";
            var rows = mode == "Day" ? report.Days : mode == "Month" ? report.Months : report.RoomTypes;
            if (rows.Count > 1048568) throw new InvalidOperationException("Báo cáo vượt số dòng Excel hỗ trợ. Hãy thu hẹp khoảng ngày.");
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    WritePackageParts(zip, "Doanh thu");
                    using (var stream = zip.CreateEntry("xl/worksheets/sheet1.xml").Open())
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                    {
                        writer.WriteStartElement("worksheet", SheetNs);
                        writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
                        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "7"); writer.WriteAttributeString("topLeftCell", "A8"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement();
                        writer.WriteEndElement(); writer.WriteEndElement();
                        writer.WriteStartElement("cols");
                        Column(writer, 1, 1, 34); Column(writer, 2, 2, 18); Column(writer, 3, 5, 32);
                        writer.WriteEndElement(); writer.WriteStartElement("sheetData");
                        TextRow(writer, 1, "MUSIC BOX — BÁO CÁO DOANH THU", 1);
                        TextRow(writer, 2, "Kỳ: " + report.FromDate.ToString("dd/MM/yyyy") + " – " + report.ToDate.ToString("dd/MM/yyyy") + " (ngày Việt Nam)");
                        TextRow(writer, 3, "Loại phòng snapshot: " + (report.RoomTypeCode ?? "Tất cả") + " • Nhóm: " + label);
                        TextRow(writer, 4, "Dữ liệu/Xuất lúc: " + report.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy") + " (UTC+7)");
                        TextRow(writer, 5, "Nguồn: hóa đơn đã thanh toán, lọc PaidAt. Số tiền trên 15 chữ số lưu dạng văn bản để giữ chính xác.");
                        writer.WriteStartElement("row"); writer.WriteAttributeString("r", "7");
                        var headers = new[] { "Nhóm doanh thu", "Số hóa đơn", "Tiền phòng (đ)", "Tiền dịch vụ (đ)", "Doanh thu (đ)" };
                        for (var i = 0; i < headers.Length; i++) Text(writer, ((char)('A' + i)).ToString() + "7", headers[i], 1);
                        writer.WriteEndElement();
                        var rowNumber = 8;
                        foreach (var row in rows)
                        {
                            writer.WriteStartElement("row"); writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
                            Text(writer, "A" + rowNumber, row.Label); Number(writer, "B" + rowNumber, row.InvoiceCount, false, false);
                            Number(writer, "C" + rowNumber, row.RoomCharge, true, false); Number(writer, "D" + rowNumber, row.ServiceCharge, true, false); Number(writer, "E" + rowNumber, row.TotalAmount, true, false);
                            writer.WriteEndElement(); rowNumber++;
                        }
                        writer.WriteStartElement("row"); writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
                        Text(writer, "A" + rowNumber, "TỔNG", 1); Number(writer, "B" + rowNumber, report.InvoiceCount, false, true);
                        Number(writer, "C" + rowNumber, report.RoomCharge, true, true); Number(writer, "D" + rowNumber, report.ServiceCharge, true, true); Number(writer, "E" + rowNumber, report.TotalAmount, true, true);
                        writer.WriteEndElement(); writer.WriteEndElement();
                        writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", "A7:E" + Math.Max(7, rowNumber - 1)); writer.WriteEndElement();
                        writer.WriteStartElement("mergeCells"); writer.WriteAttributeString("count", "5");
                        for (var r = 1; r <= 5; r++) { writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", "A" + r + ":E" + r); writer.WriteEndElement(); }
                        writer.WriteEndElement(); writer.WriteEndElement();
                    }
                }
                return new ReportExcelFile("MusicBox_DoanhThu_" + mode + "_" + report.FromDate.ToString("yyyyMMdd") + "_" + report.ToDate.ToString("yyyyMMdd") + ".xlsx", memory.ToArray());
            }
        }
        private enum Kind { Text, Count, Money, Minutes, Percent }

        internal static ReportExcelFile CreateRooms(RoomUsageReport report)
        {
            var rows = report.Rooms.Select(r => new object[] { r.RoomCode, r.RoomName, r.ActiveLabel,
                r.CompletedCount, r.UsedMinutes, r.AvailableMinutes, r.UtilizationPercent }).ToList();
            var filter = "Phòng: " + (report.RoomId.HasValue ? report.RoomChoices.Single(r => r.RoomId == report.RoomId).Label : "Tất cả, gồm phòng đã khóa");
            return CreateTable("Phong", "Sử dụng phòng", "MUSIC BOX — BÁO CÁO SỬ DỤNG PHÒNG", report.FromDate, report.ToDate, report.CheckedAt,
                filter, "Lượt theo giờ trả thực tế; tỷ lệ theo lịch mở cửa tiêu chuẩn đến thời điểm dữ liệu. Phút và tỷ lệ làm tròn 2 số thập phân.",
                new[] { "Mã phòng", "Tên phòng hiện tại", "Trạng thái", "Lượt hoàn tất", "Đã dùng (phút)", "Lịch tiêu chuẩn (phút)", "Tỷ lệ sử dụng" },
                new[] { 16, 34, 18, 18, 24, 28, 24 },
                new[] { Kind.Text, Kind.Text, Kind.Text, Kind.Count, Kind.Minutes, Kind.Minutes, Kind.Percent }, rows,
                new object[] { "TỔNG", "Tỷ lệ chung có trọng số", "", report.CompletedCount, report.UsedMinutes, report.AvailableMinutes, report.UtilizationPercent });
        }

        internal static ReportExcelFile CreateBookings(BookingStatusReport report) => CreateTable(
            "Booking", "Đặt phòng", "MUSIC BOX — BÁO CÁO ĐẶT PHÒNG", report.FromDate, report.ToDate, report.CheckedAt,
            "Trạng thái tại thời điểm dữ liệu, gồm các trạng thái có số lượt bằng 0.",
            "Lọc theo giờ bắt đầu đặt phòng. Booking đã xác nhận quá hạn nhận 15 phút tính Không đến; không sửa booking khi xuất.",
            new[] { "Trạng thái", "Số lượt đặt phòng" }, new[] { 62, 30 }, new[] { Kind.Text, Kind.Count },
            report.Rows.Select(r => new object[] { r.Label, r.Count }).ToList(), new object[] { "TỔNG", report.TotalCount });

        internal static ReportExcelFile CreateServices(ServiceSalesReport report, bool topItems)
        {
            var rows = topItems ? report.TopItems : report.Rows;
            var filter = "Dịch vụ: " + report.Services.Single(r => r.ServiceId == report.ServiceId).Label
                + " • Tab: " + (topItems ? "Top 10 theo số lượng" : "Doanh thu theo món");
            return CreateTable(topItems ? "TopMon" : "DoanhThuMon", topItems ? "Top món" : "Doanh thu món",
                topItems ? "MUSIC BOX — TOP 10 MÓN THEO SỐ LƯỢNG" : "MUSIC BOX — DOANH THU THEO MÓN",
                report.FromDate, report.ToDate, report.CheckedAt, filter,
                "Theo ngày thanh toán hóa đơn; chỉ món đã hoàn tất, dùng tên/giá đã chốt. "
                    + (topItems ? "Tổng chỉ các món trong Top 10." : "Gồm món đã ngừng bán.") + " Tiền >15 chữ số lưu dạng văn bản.",
                new[] { "Mã dịch vụ", "Tên món đã bán", "Số lượng", "Doanh thu (đ)" }, new[] { 16, 48, 22, 34 },
                new[] { Kind.Count, Kind.Text, Kind.Count, Kind.Money },
                rows.Select(r => new object[] { r.ServiceId, r.Name, r.Quantity, r.Revenue }).ToList(),
                new object[] { topItems ? "TỔNG TOP 10" : "TỔNG", "", rows.Sum(r => r.Quantity), rows.Sum(r => r.Revenue) });
        }

        private static ReportExcelFile CreateTable(string code, string sheetName, string title, DateTime from, DateTime to,
            DateTimeOffset checkedAt, string filter, string source, string[] headers, int[] widths, Kind[] kinds,
            List<object[]> rows, object[] total)
        {
            if (rows.Count > 1048568) throw new InvalidOperationException("Báo cáo vượt số dòng Excel hỗ trợ. Hãy thu hẹp khoảng ngày.");
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    WritePackageParts(zip, sheetName);
                    using (var stream = zip.CreateEntry("xl/worksheets/sheet1.xml").Open())
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                    {
                        var lastColumn = ((char)('A' + headers.Length - 1)).ToString();
                        writer.WriteStartElement("worksheet", SheetNs);
                        writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
                        writer.WriteAttributeString("showGridLines", "0");
                        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "7"); writer.WriteAttributeString("topLeftCell", "A8"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement();
                        writer.WriteEndElement(); writer.WriteEndElement();
                        writer.WriteStartElement("cols"); for (var c = 0; c < widths.Length; c++) Column(writer, c + 1, c + 1, widths[c]); writer.WriteEndElement();
                        writer.WriteStartElement("sheetData");
                        var info = new[] { title, "Kỳ: " + from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " – " + to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " (ngày Việt Nam)",
                            filter, "Dữ liệu/Xuất lúc: " + checkedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture) + " (UTC+7)", source };
                        for (var r = 0; r < info.Length; r++)
                        {
                            writer.WriteStartElement("row"); writer.WriteAttributeString("r", (r + 1).ToString()); writer.WriteAttributeString("ht", "32"); writer.WriteAttributeString("customHeight", "1");
                            Text(writer, "A" + (r + 1), info[r], r == 0 ? 1 : 10); writer.WriteEndElement();
                        }
                        writer.WriteStartElement("row"); writer.WriteAttributeString("r", "7");
                        for (var c = 0; c < headers.Length; c++) Text(writer, ((char)('A' + c)) + "7", headers[c], 1);
                        writer.WriteEndElement();
                        var rowNumber = 8;
                        foreach (var row in rows) { WriteDataRow(writer, rowNumber++, row, kinds, widths, false); }
                        WriteDataRow(writer, rowNumber, total, kinds, widths, true);
                        writer.WriteEndElement(); writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", "A7:" + lastColumn + Math.Max(7, rowNumber - 1)); writer.WriteEndElement();
                        writer.WriteStartElement("mergeCells"); writer.WriteAttributeString("count", "5");
                        for (var r = 1; r <= 5; r++) { writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", "A" + r + ":" + lastColumn + r); writer.WriteEndElement(); }
                        writer.WriteEndElement(); writer.WriteEndElement();
                    }
                }
                return new ReportExcelFile("MusicBox_" + code + "_" + from.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + to.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx", memory.ToArray());
            }
        }

        private static void WriteDataRow(XmlWriter writer, int row, object[] values, Kind[] kinds, int[] widths, bool bold)
        {
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
            var lines = 1;
            for (var c = 0; c < values.Length; c++)
                if (values[c] is string && kinds[c] == Kind.Text)
                    lines = Math.Max(lines, (int)Math.Ceiling(((string)values[c]).Length / Math.Max(6.0, widths[c] / 1.3)));
            writer.WriteAttributeString("ht", (lines * 15 + 6).ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("customHeight", "1");
            for (var c = 0; c < values.Length; c++)
            {
                var cell = ((char)('A' + c)).ToString() + row;
                if (values[c] == null) { Text(writer, cell, "Không có dữ liệu", bold ? 1 : 0); continue; }
                if (values[c] is string || kinds[c] == Kind.Text) { Text(writer, cell, Convert.ToString(values[c], CultureInfo.InvariantCulture), bold ? 1 : 10); continue; }
                var number = Convert.ToDecimal(values[c], CultureInfo.InvariantCulture);
                if (kinds[c] == Kind.Count || kinds[c] == Kind.Money) { Number(writer, cell, number, kinds[c] == Kind.Money, bold); continue; }
                var value = decimal.Round(number, 2, MidpointRounding.AwayFromZero);
                if (kinds[c] == Kind.Percent) value /= 100m;
                writer.WriteStartElement("c"); writer.WriteAttributeString("r", cell); writer.WriteAttributeString("t", "n");
                writer.WriteAttributeString("s", (kinds[c] == Kind.Percent ? (bold ? 9 : 8) : (bold ? 7 : 6)).ToString());
                writer.WriteElementString("v", value.ToString(CultureInfo.InvariantCulture)); writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static void WritePackageParts(ZipArchive zip, string sheetName)
        {
                    Part(zip, "[Content_Types].xml", @"<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/><Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/><Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/></Types>");
                    Part(zip, "_rels/.rels", @"<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>");
                    Part(zip, "xl/workbook.xml", @"<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets><sheet name=""Doanh thu"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>".Replace("Doanh thu", sheetName));
                    Part(zip, "xl/_rels/workbook.xml.rels", @"<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/></Relationships>");
                    Part(zip, "xl/styles.xml", @"<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><numFmts count=""3""><numFmt numFmtId=""164"" formatCode=""#,##0&quot; đ&quot;""/><numFmt numFmtId=""165"" formatCode=""#,##0.00""/><numFmt numFmtId=""166"" formatCode=""0.00%""/></numFmts><fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""2""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill></fills><borders count=""1""><border/></borders><cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs><cellXfs count=""11""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""164"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""164"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1"" applyFont=""1""/><xf numFmtId=""49"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""49"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1"" applyFont=""1""/><xf numFmtId=""165"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""165"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""166"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""166"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyAlignment=""1""><alignment wrapText=""1"" vertical=""center""/></xf></cellXfs><cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles></styleSheet>");
        }
        private static void Part(ZipArchive zip, string name, string xml)
        { using (var stream = zip.CreateEntry(name).Open()) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(xml); }
        private static void Column(XmlWriter writer, int min, int max, int width)
        {
            writer.WriteStartElement("col"); writer.WriteAttributeString("min", min.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("max", max.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("customWidth", "1"); writer.WriteEndElement();
        }
        private static void TextRow(XmlWriter writer, int row, string text, int style = 0)
        { writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture)); Text(writer, "A" + row, text, style); writer.WriteEndElement(); }
        private static void Text(XmlWriter writer, string cell, string value, int style = 0)
        {
            writer.WriteStartElement("c"); writer.WriteAttributeString("r", cell); writer.WriteAttributeString("t", "inlineStr"); writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("is"); writer.WriteStartElement("t"); writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve"); writer.WriteString(value ?? "");
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        }
        private static void Number(XmlWriter writer, string cell, decimal value, bool money, bool bold)
        {
            var text = value.ToString("0", CultureInfo.InvariantCulture);
            if (text.TrimStart('-').Length > 15) { Text(writer, cell, text + (money ? " đ" : ""), bold ? 5 : 4); return; }
            writer.WriteStartElement("c"); writer.WriteAttributeString("r", cell); writer.WriteAttributeString("t", "n"); writer.WriteAttributeString("s", (money ? (bold ? 3 : 2) : (bold ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("v", text); writer.WriteEndElement();
        }
    }
}
