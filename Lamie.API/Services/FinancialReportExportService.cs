using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Xml;
using Lamie.Application.Reports;

namespace Lamie.API.Services;

public sealed class FinancialReportExportService : IFinancialReportExportService
{
    private const string SpreadsheetNamespace = "urn:schemas-microsoft-com:office:spreadsheet";
    private const string SpreadsheetPrefix = "ss";
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public ReportFileDto CreateExcel(FinancialReportDto report)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(true),
            Indent = true,
            CloseOutput = false
        }))
        {
            writer.WriteStartDocument();
            writer.WriteProcessingInstruction("mso-application", "progid=\"Excel.Sheet\"");
            writer.WriteStartElement("Workbook", SpreadsheetNamespace);
            writer.WriteAttributeString("xmlns", SpreadsheetPrefix, null, SpreadsheetNamespace);
            writer.WriteAttributeString("xmlns", "o", null, "urn:schemas-microsoft-com:office:office");
            writer.WriteAttributeString("xmlns", "x", null, "urn:schemas-microsoft-com:office:excel");
            WriteStyles(writer);
            WriteSummarySheet(writer, report);
            WriteTimelineSheet(writer, report);
            WriteCategorySheet(writer, report);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return new ReportFileDto(
            stream.ToArray(),
            "application/vnd.ms-excel",
            $"bao-cao-tai-chinh-{report.Period.From:yyyy-MM-dd}-{report.Period.To:yyyy-MM-dd}.xls");
    }

    public ReportFileDto CreatePrintableHtml(FinancialReportDto report)
    {
        Func<string, string> encode = value => HtmlEncoder.Default.Encode(value);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>Báo cáo tài chính Lamie</title>");
        html.Append("<style>");
        html.Append("body{font-family:Arial,sans-serif;color:#292623;margin:32px;font-size:12px}");
        html.Append("h1{font-size:24px;margin:0 0 6px}h2{font-size:16px;margin:28px 0 8px}");
        html.Append("p{line-height:1.5}.meta{color:#625d57;margin:0 0 24px}");
        html.Append(".totals{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin:18px 0}");
        html.Append(".total{border:1px solid #ded8d1;padding:14px}.label{color:#625d57}.value{font-size:20px;font-weight:700;margin-top:6px}");
        html.Append("table{width:100%;border-collapse:collapse;margin-top:8px}th,td{border-bottom:1px solid #ded8d1;padding:8px;text-align:left}");
        html.Append("th{background:#f1efeb}.number{text-align:right;font-variant-numeric:tabular-nums}.note{color:#625d57;margin-top:18px}");
        html.Append("@page{size:A4 landscape;margin:14mm}@media print{body{margin:0}.no-print{display:none}}");
        html.Append("</style></head><body>");
        html.Append("<h1>Báo cáo doanh thu, chi phí &amp; lợi nhuận</h1>");
        html.Append($"<p class=\"meta\">Kỳ {encode(FormatDate(report.Period.From))} - {encode(FormatDate(report.Period.To))}. Tạo lúc {encode(report.GeneratedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", VietnameseCulture))}.</p>");
        html.Append("<section class=\"totals\">");
        AppendTotal(html, "Doanh thu", report.Revenue);
        AppendTotal(html, "Tổng chi phí", report.MaterialProductCogs + report.MaterialWaste + report.Expense);
        AppendTotal(html, report.IsProfitProvisional ? "Lợi nhuận tạm tính" : "Lợi nhuận trước thuế", report.Profit);
        html.Append("</section>");
        html.Append($"<p class=\"meta\">Phí giao hàng {encode(report.IncludeShippingFeeInRevenue ? "được tính" : "không được tính")} vào doanh thu.</p>");
        html.Append("<h2>Diễn biến theo kỳ</h2><table><thead><tr><th>Kỳ</th><th class=\"number\">Doanh thu</th><th class=\"number\">Tiền hoa</th><th class=\"number\">Ship thu khách</th><th class=\"number\">Ship thực tế</th><th class=\"number\">Chi phí khác</th><th class=\"number\">Chênh lệch tạm tính</th><th class=\"number\">Số đơn</th></tr></thead><tbody>");
        foreach (var point in report.Points)
        {
            html.Append("<tr>");
            html.Append($"<td>{encode(point.Label)}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.Revenue))}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.ProductRevenue))}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.ShippingFee))}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.ShippingFeeActual))}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.OtherExpense))}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(point.Profit))}</td>");
            html.Append($"<td class=\"number\">{point.OrderCount}</td>");
            html.Append("</tr>");
        }
        html.Append("</tbody></table>");
        html.Append("<h2>Tổng hợp giá vốn và lợi nhuận kỳ</h2><table><tbody>");
        AppendSummaryRow(html, "Giá vốn nguyên liệu vào sản phẩm", report.MaterialProductCogs);
        AppendSummaryRow(html, "Hoa hỏng / hao hụt", report.MaterialWaste);
        AppendSummaryRow(html, "Phí ship thực tế", report.ShippingFeeActual);
        AppendSummaryRow(html, "Chi phí vận hành", report.OtherExpense);
        AppendSummaryRow(html, report.IsProfitProvisional ? "Lợi nhuận tạm tính" : "Lợi nhuận trước thuế", report.Profit);
        html.Append("</tbody></table>");
        if (report.ProfitProvisionalReasons.Count > 0)
            html.Append($"<p class=\"note\"><strong>Số liệu tạm tính:</strong> {encode(string.Join(" ", report.ProfitProvisionalReasons))}</p>");
        html.Append("<h2>Chi phí theo danh mục</h2><table><thead><tr><th>Danh mục</th><th class=\"number\">Số khoản</th><th class=\"number\">Tổng chi</th></tr></thead><tbody>");
        foreach (var category in report.ExpensesByCategory)
        {
            html.Append("<tr>");
            html.Append($"<td>{encode(category.ExpenseCategoryName)}</td>");
            html.Append($"<td class=\"number\">{category.ExpenseCount}</td>");
            html.Append($"<td class=\"number\">{encode(FormatMoney(category.TotalAmount))}</td>");
            html.Append("</tr>");
        }
        if (report.ExpensesByCategory.Count == 0)
            html.Append("<tr><td colspan=\"3\">Không có chi phí trong kỳ.</td></tr>");
        html.Append("</tbody></table>");
        html.Append($"<p class=\"note\">{encode(report.RevenueBasis)} {encode(report.ProfitBasis)}</p>");
        html.Append("<script>window.addEventListener('load',function(){window.print();});</script>");
        html.Append("</body></html>");

        return new ReportFileDto(
            Encoding.UTF8.GetBytes(html.ToString()),
            "text/html; charset=utf-8",
            $"bao-cao-tai-chinh-{report.Period.From:yyyy-MM-dd}-{report.Period.To:yyyy-MM-dd}.html");
    }

    private static void WriteStyles(XmlWriter writer)
    {
        writer.WriteStartElement("Styles", SpreadsheetNamespace);
        writer.WriteStartElement("Style", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "ID", SpreadsheetNamespace, "Default");
        writer.WriteEndElement();
        writer.WriteStartElement("Style", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "ID", SpreadsheetNamespace, "Header");
        writer.WriteStartElement("Font", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "Bold", SpreadsheetNamespace, "1");
        writer.WriteEndElement();
        writer.WriteStartElement("Interior", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "Color", SpreadsheetNamespace, "#F1EFEB");
        writer.WriteAttributeString(SpreadsheetPrefix, "Pattern", SpreadsheetNamespace, "Solid");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("Style", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "ID", SpreadsheetNamespace, "Money");
        writer.WriteStartElement("NumberFormat", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "Format", SpreadsheetNamespace, "#,##0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("Style", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "ID", SpreadsheetNamespace, "Percent");
        writer.WriteStartElement("NumberFormat", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "Format", SpreadsheetNamespace, "0.00");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteSummarySheet(XmlWriter writer, FinancialReportDto report)
    {
        StartSheet(writer, "Tổng hợp");
        WriteRow(writer, [StringCell("Chỉ số", "Header"), StringCell("Giá trị", "Header")]);
        WriteRow(writer, [StringCell("Từ ngày"), StringCell(report.Period.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        WriteRow(writer, [StringCell("Đến ngày"), StringCell(report.Period.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        WriteRow(writer, [StringCell("Tính phí giao hàng vào doanh thu"), StringCell(report.IncludeShippingFeeInRevenue ? "Có" : "Không")]);
        WriteRow(writer, [StringCell("Doanh thu"), NumberCell(report.Revenue, "Money")]);
        WriteRow(writer, [StringCell("Tiền sản phẩm"), NumberCell(report.ProductRevenue, "Money")]);
        WriteRow(writer, [StringCell("Phí giao hàng"), NumberCell(report.ShippingFee, "Money")]);
        WriteRow(writer, [StringCell("Phí ship thực tế"), NumberCell(report.ShippingFeeActual, "Money")]);
        WriteRow(writer, [StringCell("Chi phí khác"), NumberCell(report.OtherExpense, "Money")]);
        WriteRow(writer, [StringCell("Giá vốn nguyên liệu vào sản phẩm"), NumberCell(report.MaterialProductCogs, "Money")]);
        WriteRow(writer, [StringCell("Hoa hỏng / hao hụt"), NumberCell(report.MaterialWaste, "Money")]);
        WriteRow(writer, [StringCell(report.IsProfitProvisional ? "Lợi nhuận tạm tính" : "Lợi nhuận trước thuế"), NumberCell(report.Profit, "Money")]);
        WriteRow(writer, [StringCell("Biên lợi nhuận (%)"), report.ProfitMarginPercent.HasValue ? NumberCell(report.ProfitMarginPercent.Value, "Percent") : StringCell("Không xác định")]);
        WriteRow(writer, [StringCell("Trạng thái lợi nhuận"), StringCell(report.IsProfitProvisional ? "Tạm tính" : "Đã xác nhận")]);
        if (report.ProfitProvisionalReasons.Count > 0)
            WriteRow(writer, [StringCell("Lý do tạm tính"), StringCell(string.Join(" ", report.ProfitProvisionalReasons))]);
        WriteRow(writer, [StringCell("Số đơn đã tính doanh thu"), NumberCell(report.OrderCount)]);
        WriteRow(writer, [StringCell("Số khoản chi"), NumberCell(report.ExpenseCount)]);
        WriteRow(writer, [StringCell("Cơ sở doanh thu"), StringCell(report.RevenueBasis)]);
        WriteRow(writer, [StringCell("Cơ sở chênh lệch tạm tính"), StringCell(report.ProfitBasis)]);
        EndSheet(writer);
    }

    private static void WriteTimelineSheet(XmlWriter writer, FinancialReportDto report)
    {
        StartSheet(writer, "Theo kỳ");
        WriteRow(writer,
        [
            StringCell("Kỳ", "Header"),
            StringCell("Từ ngày", "Header"),
            StringCell("Đến ngày", "Header"),
            StringCell("Doanh thu", "Header"),
            StringCell("Tiền sản phẩm", "Header"),
            StringCell("Phí giao hàng", "Header"),
            StringCell("Phí ship thực tế", "Header"),
            StringCell("Chi phí khác", "Header"),
            StringCell("Chênh lệch tạm tính", "Header"),
            StringCell("Số đơn", "Header"),
            StringCell("Số khoản chi", "Header")
        ]);
        foreach (var point in report.Points)
        {
            WriteRow(writer,
            [
                StringCell(point.Label),
                StringCell(point.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                StringCell(point.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                NumberCell(point.Revenue, "Money"),
                NumberCell(point.ProductRevenue, "Money"),
                NumberCell(point.ShippingFee, "Money"),
                NumberCell(point.ShippingFeeActual, "Money"),
                NumberCell(point.OtherExpense, "Money"),
                NumberCell(point.Profit, "Money"),
                NumberCell(point.OrderCount),
                NumberCell(point.ExpenseCount)
            ]);
        }
        EndSheet(writer);
    }

    private static void WriteCategorySheet(XmlWriter writer, FinancialReportDto report)
    {
        StartSheet(writer, "Chi phí theo danh mục");
        WriteRow(writer,
        [
            StringCell("Danh mục", "Header"),
            StringCell("Số khoản", "Header"),
            StringCell("Tổng chi", "Header")
        ]);
        foreach (var category in report.ExpensesByCategory)
        {
            WriteRow(writer,
            [
                StringCell(category.ExpenseCategoryName),
                NumberCell(category.ExpenseCount),
                NumberCell(category.TotalAmount, "Money")
            ]);
        }
        EndSheet(writer);
    }

    private static void StartSheet(XmlWriter writer, string name)
    {
        writer.WriteStartElement("Worksheet", SpreadsheetNamespace);
        writer.WriteAttributeString(SpreadsheetPrefix, "Name", SpreadsheetNamespace, name);
        writer.WriteStartElement("Table", SpreadsheetNamespace);
    }

    private static void EndSheet(XmlWriter writer)
    {
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteRow(XmlWriter writer, IReadOnlyCollection<SpreadsheetCell> cells)
    {
        writer.WriteStartElement("Row", SpreadsheetNamespace);
        foreach (var cell in cells)
        {
            writer.WriteStartElement("Cell", SpreadsheetNamespace);
            if (!string.IsNullOrWhiteSpace(cell.Style))
                writer.WriteAttributeString(SpreadsheetPrefix, "StyleID", SpreadsheetNamespace, cell.Style);
            writer.WriteStartElement("Data", SpreadsheetNamespace);
            writer.WriteAttributeString(SpreadsheetPrefix, "Type", SpreadsheetNamespace, cell.Type);
            writer.WriteString(cell.Value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static SpreadsheetCell StringCell(string value, string? style = null) =>
        new("String", value, style);

    private static SpreadsheetCell NumberCell(decimal value, string? style = null) =>
        new("Number", value.ToString(CultureInfo.InvariantCulture), style);

    private static SpreadsheetCell NumberCell(int value, string? style = null) =>
        new("Number", value.ToString(CultureInfo.InvariantCulture), style);

    private static void AppendTotal(StringBuilder html, string label, decimal value)
    {
        html.Append("<div class=\"total\">");
        html.Append($"<div class=\"label\">{HtmlEncoder.Default.Encode(label)}</div>");
        html.Append($"<div class=\"value\">{HtmlEncoder.Default.Encode(FormatMoney(value))}</div>");
        html.Append("</div>");
    }

    private static void AppendSummaryRow(StringBuilder html, string label, decimal value)
    {
        html.Append("<tr>");
        html.Append($"<td>{HtmlEncoder.Default.Encode(label)}</td>");
        html.Append($"<td class=\"number\">{HtmlEncoder.Default.Encode(FormatMoney(value))}</td>");
        html.Append("</tr>");
    }

    private static string FormatMoney(decimal value) => $"{value.ToString("N0", VietnameseCulture)} ₫";

    private static string FormatDate(DateOnly value) => value.ToString("dd/MM/yyyy", VietnameseCulture);

    private sealed record SpreadsheetCell(string Type, string Value, string? Style);
}
