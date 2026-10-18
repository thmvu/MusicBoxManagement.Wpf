using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Minimal Open XML workbook, written with framework libraries; no Excel installation.
    // No formulas/macros/external links. Untrusted labels are inline text cells.
    internal static class RevenueExcelWriter
    {
        private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        internal static ReportExcelFile Create(RevenueReport report, string mode)
        {
            var label = mode == "Day" ? "Theo ngày" : mode == "Month" ? "Theo tháng" : "Theo loại phòng";
            var rows = mode == "Day" ? report.Days : mode == "Month" ? report.Months : report.RoomTypes;
            if (rows.Count > 1048568) throw new InvalidOperationException("Báo cáo vượt số dòng Excel hỗ trợ. Hãy thu hẹp khoảng ngày.");
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    Part(zip, "[Content_Types].xml", @"<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/><Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/><Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/></Types>");
                    Part(zip, "_rels/.rels", @"<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>");
                    Part(zip, "xl/workbook.xml", @"<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets><sheet name=""Doanh thu"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>");
                    Part(zip, "xl/_rels/workbook.xml.rels", @"<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/></Relationships>");
                    Part(zip, "xl/styles.xml", @"<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><numFmts count=""1""><numFmt numFmtId=""164"" formatCode=""#,##0&quot; đ&quot;""/></numFmts><fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""2""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill></fills><borders count=""1""><border/></borders><cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs><cellXfs count=""6""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""164"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""164"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1"" applyFont=""1""/><xf numFmtId=""49"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""49"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1"" applyFont=""1""/></cellXfs><cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles></styleSheet>");
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
