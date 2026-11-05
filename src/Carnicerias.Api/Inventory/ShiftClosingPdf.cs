using System.Globalization;
using System.Net;
using System.Text;
using PeachPDF;

namespace Carnicerias.Api.Inventory;

public static class ShiftClosingPdf
{
    private static readonly CultureInfo Argentine = CultureInfo.GetCultureInfo("es-AR");

    public static async Task<byte[]> RenderAsync(ShiftClosingSnapshot report)
    {
        var generator = new PdfGenerator();
        var document = await generator.GeneratePdf(ToHtml(report), new PdfGenerateConfig
        {
            PageSize = PageSize.A4,
            PageOrientation = PageOrientation.Portrait
        });
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    public static string ToHtml(ShiftClosingSnapshot report)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><style>
            body{font-family:Arial,sans-serif;color:#202020;font-size:12px;margin:24px}
            h1{font-size:20px;margin:0 0 6px}h2{font-size:15px;margin:20px 0 6px}
            table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ddd;padding:5px;text-align:left}
            td:last-child,th:last-child{text-align:right}small{color:#555}
            </style></head><body>
            <h1>Rendición de turno</h1><p>Comprobante interno no fiscal</p>
            """);
        html.Append("<p><strong>").Append(E(report.CompanyName)).Append("</strong><br>")
            .Append(E(report.BranchName)).Append(" · ").Append(E(report.TerminalName ?? "Caja histórica"))
            .Append("<br>Cajero: ").Append(E(report.CashierName))
            .Append("<br>Turno: ").Append(report.ShiftId)
            .Append("<br>Apertura: ").Append(Date(report.OpenedAtUtc))
            .Append("<br>Cierre: ").Append(Date(report.ClosedAtUtc)).Append("</p>");
        html.Append("<h2>Movimientos</h2><table><tbody>");
        Row(html, "Fondo inicial", report.OpeningCash);
        Row(html, "Ventas del turno", report.SalesTotal);
        Row(html, "Ventas cobradas en efectivo", report.CashSales);
        Row(html, "Ventas cobradas por otros medios", report.NonCashSales);
        Row(html, "Ventas a cuenta corriente", report.AccountSales);
        Row(html, "Saldo a favor aplicado", report.CreditApplied);
        Row(html, "Cobranzas de cuenta en efectivo", report.CashCollections);
        Row(html, "Cobranzas de cuenta por otros medios", report.NonCashCollections);
        foreach (var payment in report.PaymentBreakdown)
            Row(html, "Cobros netos · " + PaymentName(payment.Method), payment.Amount);
        Row(html, "Retiros extraordinarios", report.CashWithdrawals);
        Row(html, "Efectivo esperado en caja", report.ExpectedCash);
        if (report.CountedCash.HasValue)
        {
            Row(html, "Efectivo contado", report.CountedCash.Value);
            Row(html, "Efectivo entregado", report.HandedCash!.Value);
            Row(html, "Diferencia de arqueo", report.CashDifference!.Value);
        }
        else html.Append("<tr><td colspan=\"2\">Cierre anterior sin arqueo declarado.</td></tr>");
        html.Append("</tbody></table><p><small>Informe fijo al cerrar el turno. Versión ")
            .Append(report.Version).Append(".</small></p></body></html>");
        return html.ToString();
    }

    private static void Row(StringBuilder html, string label, decimal amount) =>
        html.Append("<tr><td>").Append(E(label)).Append("</td><td>$ ")
            .Append(amount.ToString("N2", Argentine)).Append("</td></tr>");

    private static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Argentine);

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string PaymentName(string method) => method switch
    {
        "cash" => "Efectivo", "debit" => "Débito", "credit" => "Crédito",
        "transfer" => "Transferencia", "mercadoPago" => "Mercado Pago",
        "cheque" => "Cheque", _ => method
    };
}
