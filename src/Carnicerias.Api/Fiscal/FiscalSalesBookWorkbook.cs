using System.Globalization;
using ClosedXML.Excel;

namespace Carnicerias.Api.Fiscal;

public sealed record FiscalVatBreakdown(int ArcaRateCode, decimal RatePercent,
    decimal TaxableBase, decimal TaxAmount);

public sealed record FiscalSalesBookItem(Guid FiscalDocumentId, DateOnly IssueDate,
    string BranchName, string DocumentType, int VoucherType, int PointOfSale,
    long Number, string ReceiverDocument, string? RecipientName,
    int? ReceiverVatConditionCode, string Cae, string SnapshotStatus,
    decimal Total, decimal? TaxableBase, decimal? VatAmount,
    decimal? ExemptAmount, decimal? NotTaxedAmount,
    IReadOnlyList<FiscalVatBreakdown> VatBreakdown);

/// <summary>Operational Excel export of authorized fiscal attempts, without inferred historical taxes.</summary>
public static class FiscalSalesBookWorkbook
{
    public static byte[] Create(IReadOnlyList<FiscalSalesBookItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var complete = items.Where(item => item.SnapshotStatus == "complete").ToArray();
        var pending = items.Where(item => item.SnapshotStatus != "complete").ToArray();
        var rates = complete.SelectMany(item => item.VatBreakdown)
            .Select(item => (item.ArcaRateCode, item.RatePercent))
            .Distinct().OrderBy(item => item.ArcaRateCode).ThenBy(item => item.RatePercent)
            .ToArray();
        using var workbook = new XLWorkbook();
        var issued = workbook.AddWorksheet("Completos");
        var pendingSheet = workbook.AddWorksheet("Pendientes");
        var fixedHeaders = new[]
        {
            "Fecha", "Sucursal", "Comprobante", "Punto de venta", "Número",
            "Documento receptor", "Razón social", "Condición IVA", "CAE"
        };
        for (var column = 1; column <= fixedHeaders.Length; column++)
            issued.Cell(1, column).Value = fixedHeaders[column - 1];
        for (var index = 0; index < rates.Length; index++)
        {
            var rateLabel = rates[index].RatePercent.ToString("0.##",
                CultureInfo.GetCultureInfo("es-AR"));
            issued.Cell(1, 10 + index * 2).Value = $"Neto {rateLabel} %";
            issued.Cell(1, 11 + index * 2).Value = $"IVA {rateLabel} %";
        }
        var exemptColumn = 10 + rates.Length * 2;
        issued.Cell(1, exemptColumn).Value = "Exento";
        issued.Cell(1, exemptColumn + 1).Value = "No gravado";
        issued.Cell(1, exemptColumn + 2).Value = "Otros tributos";
        issued.Cell(1, exemptColumn + 3).Value = "Total";
        for (var index = 0; index < complete.Length; index++)
        {
            var item = complete[index];
            var row = index + 2;
            WriteIdentity(issued, row, item);
            for (var rateIndex = 0; rateIndex < rates.Length; rateIndex++)
            {
                var rate = rates[rateIndex];
                var vat = item.VatBreakdown.SingleOrDefault(line =>
                    line.ArcaRateCode == rate.ArcaRateCode &&
                    line.RatePercent == rate.RatePercent);
                issued.Cell(row, 10 + rateIndex * 2).Value = vat?.TaxableBase ?? 0m;
                issued.Cell(row, 11 + rateIndex * 2).Value = vat?.TaxAmount ?? 0m;
            }
            issued.Cell(row, exemptColumn).Value = item.ExemptAmount ?? 0m;
            issued.Cell(row, exemptColumn + 1).Value = item.NotTaxedAmount ?? 0m;
            issued.Cell(row, exemptColumn + 2).Value = "No modelado";
            issued.Cell(row, exemptColumn + 3).Value = item.Total;
        }
        var totalRow = complete.Length + 2;
        issued.Cell(totalRow, 1).Value = "Totales completos";
        for (var rateIndex = 0; rateIndex < rates.Length; rateIndex++)
        {
            var rate = rates[rateIndex];
            issued.Cell(totalRow, 10 + rateIndex * 2).Value = complete.Sum(item =>
                item.VatBreakdown.Where(line => line.ArcaRateCode == rate.ArcaRateCode &&
                    line.RatePercent == rate.RatePercent).Sum(line => line.TaxableBase));
            issued.Cell(totalRow, 11 + rateIndex * 2).Value = complete.Sum(item =>
                item.VatBreakdown.Where(line => line.ArcaRateCode == rate.ArcaRateCode &&
                    line.RatePercent == rate.RatePercent).Sum(line => line.TaxAmount));
        }
        issued.Cell(totalRow, exemptColumn).Value = complete.Sum(item => item.ExemptAmount ?? 0m);
        issued.Cell(totalRow, exemptColumn + 1).Value = complete.Sum(item => item.NotTaxedAmount ?? 0m);
        issued.Cell(totalRow, exemptColumn + 2).Value = "No modelado";
        issued.Cell(totalRow, exemptColumn + 3).Value = complete.Sum(item => item.Total);
        Format(issued, totalRow, exemptColumn + 3);

        for (var column = 1; column <= fixedHeaders.Length; column++)
            pendingSheet.Cell(1, column).Value = fixedHeaders[column - 1];
        pendingSheet.Cell(1, 10).Value = "Total conocido";
        pendingSheet.Cell(1, 11).Value = "Pendiente de revisión";
        for (var index = 0; index < pending.Length; index++)
        {
            var row = index + 2;
            WriteIdentity(pendingSheet, row, pending[index]);
            pendingSheet.Cell(row, 10).Value = pending[index].Total;
            pendingSheet.Cell(row, 11).Value = pending[index].SnapshotStatus == "inconsistent"
                ? "Snapshot fiscal no coincide con el comprobante"
                : "Snapshot fiscal anterior incompleto";
        }
        var pendingTotalRow = pending.Length + 2;
        pendingSheet.Cell(pendingTotalRow, 1).Value = "Total pendiente conocido";
        pendingSheet.Cell(pendingTotalRow, 10).Value = pending.Sum(item => item.Total);
        Format(pendingSheet, pendingTotalRow, 11);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteIdentity(IXLWorksheet sheet, int row, FiscalSalesBookItem item)
    {
        sheet.Cell(row, 1).Value = item.IssueDate.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
        sheet.Cell(row, 2).Value = item.BranchName;
        var kind = item.DocumentType == "fiscalTicket"
            ? "Ticket fiscal" : "Factura electrónica";
        var voucher = item.VoucherType switch
        {
            1 => "A", 6 => "B", 11 => "C",
            _ => $"tipo {item.VoucherType.ToString(CultureInfo.InvariantCulture)}"
        };
        sheet.Cell(row, 3).Value = $"{kind} {voucher}";
        sheet.Cell(row, 4).Value = item.PointOfSale;
        sheet.Cell(row, 5).Value = item.Number;
        sheet.Cell(row, 6).Value = item.ReceiverDocument;
        sheet.Cell(row, 7).Value = item.RecipientName ?? string.Empty;
        sheet.Cell(row, 8).Value = item.ReceiverVatConditionCode switch
        {
            1 => "Responsable inscripto (1)",
            4 => "Exento (4)",
            5 => "Consumidor final (5)",
            6 => "Monotributista (6)",
            null => string.Empty,
            _ => item.ReceiverVatConditionCode.Value.ToString(CultureInfo.InvariantCulture)
        };
        sheet.Cell(row, 9).Value = item.Cae;
    }

    private static void Format(IXLWorksheet sheet, int totalRow, int lastColumn)
    {
        sheet.Range(1, 1, 1, lastColumn).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, lastColumn).Style.Fill.BackgroundColor = XLColor.Teal;
        sheet.Range(1, 1, 1, lastColumn).Style.Font.FontColor = XLColor.White;
        sheet.Range(totalRow, 1, totalRow, lastColumn).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, lastColumn).AdjustToContents();
        sheet.Columns(10, lastColumn).Style.NumberFormat.Format = "#,##0.00";
    }
}
