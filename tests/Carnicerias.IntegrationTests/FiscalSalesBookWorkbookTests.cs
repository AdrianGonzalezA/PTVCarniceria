using Carnicerias.Api.Fiscal;
using ClosedXML.Excel;

namespace Carnicerias.IntegrationTests;

public sealed class FiscalSalesBookWorkbookTests
{
    [Fact]
    public void ExportSeparatesAuthorizedLegacyDocumentsAndTotalsMixedVatWithoutDuplicatingInvoices()
    {
        var complete = new FiscalSalesBookItem(Guid.NewGuid(), new DateOnly(2026, 11, 4),
            "Alta Córdoba", "electronicInvoice", 6, 99, 25, "20123456789",
            "Cliente", 5, "12345678901234", "complete", 352.5m,
            300m, 52.5m, 0m, 0m,
            [new FiscalVatBreakdown(4, 10.5m, 100m, 10.5m),
             new FiscalVatBreakdown(5, 21m, 200m, 42m)]);
        var legacy = new FiscalSalesBookItem(Guid.NewGuid(), new DateOnly(2026, 11, 4),
            "Alta Córdoba", "fiscalTicket", 6, 99, 24, "0",
            null, null, "12345678901235", "legacyIncomplete", 150m,
            null, null, null, null, []);

        var bytes = FiscalSalesBookWorkbook.Create([complete, legacy]);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var issued = workbook.Worksheet("Completos");
        var pending = workbook.Worksheet("Pendientes");

        Assert.Equal("Neto 10,5 %", issued.Cell(1, 10).GetString());
        Assert.Equal("Factura electrónica B", issued.Cell(2, 3).GetString());
        Assert.Equal("Consumidor final (5)", issued.Cell(2, 8).GetString());
        Assert.Equal("IVA 10,5 %", issued.Cell(1, 11).GetString());
        Assert.Equal("Neto 21 %", issued.Cell(1, 12).GetString());
        Assert.Equal(100m, issued.Cell(2, 10).GetValue<decimal>());
        Assert.Equal(42m, issued.Cell(2, 13).GetValue<decimal>());
        Assert.Equal(352.5m, issued.Cell(2, 17).GetValue<decimal>());
        Assert.Equal(352.5m, issued.Cell(3, 17).GetValue<decimal>());
        Assert.Equal("No modelado", issued.Cell(2, 16).GetString());
        Assert.Equal(150m, pending.Cell(2, 10).GetValue<decimal>());
        Assert.Equal("Snapshot fiscal anterior incompleto", pending.Cell(2, 11).GetString());
    }

    [Fact]
    public void MarksAConflictingSnapshotForReviewWithoutInventingTaxAmounts()
    {
        var inconsistent = new FiscalSalesBookItem(Guid.NewGuid(), new DateOnly(2026, 11, 5),
            "Sucursal", "fiscalTicket", 1, 99, 26, "0", null, null,
            "12345678901234", "inconsistent", 200m, null, null, null, null, []);

        using var workbook = new XLWorkbook(new MemoryStream(
            FiscalSalesBookWorkbook.Create([inconsistent])));
        var pending = workbook.Worksheet("Pendientes");
        Assert.Equal("Ticket fiscal A", pending.Cell(2, 3).GetString());
        Assert.Equal("Snapshot fiscal no coincide con el comprobante",
            pending.Cell(2, 11).GetString());
        Assert.Equal(200m, pending.Cell(3, 10).GetValue<decimal>());
    }
}
