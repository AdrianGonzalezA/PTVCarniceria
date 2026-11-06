using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Inventory;

public sealed record TransferRemitoImportPlan(InventoryTransferReceipt? Receipt,
    IReadOnlyList<InventoryTransferReceiptLine> Lines, string? Error);

/// <summary>Parses an unsigned export as declared data, never as proof of dispatch.</summary>
public static class TransferRemitoImportPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TransferRemitoImportPlan Plan(JsonElement payload, Guid companyId,
        Guid destinationBranchId, string companyCode, string destinationBranchName,
        IReadOnlyList<CatalogProduct> products, Guid operationId, Guid actorId,
        DateTimeOffset now)
    {
        if (payload.ValueKind != JsonValueKind.Object || operationId == Guid.Empty)
            return Invalid("INVALID_REMITO");
        var raw = payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > 1_000_000)
            return Invalid("REMITO_TOO_LARGE");
        TransferExport? export;
        try { export = JsonSerializer.Deserialize<TransferExport>(raw, JsonOptions); }
        catch (JsonException) { return Invalid("INVALID_REMITO"); }
        if (export is null || export.SchemaVersion != 1 || export.TransferId == Guid.Empty ||
            export.OperationId == Guid.Empty || export.DocumentDate == default ||
            export.DispatchedAtUtc == default || export.OriginBranchId == Guid.Empty ||
            !string.Equals(export.CompanyCode?.Trim(), companyCode,
                StringComparison.OrdinalIgnoreCase) ||
            (export.DestinationBranchId.HasValue
                ? export.DestinationBranchId != destinationBranchId
                : !string.Equals(export.DestinationName?.Trim(), destinationBranchName,
                    StringComparison.OrdinalIgnoreCase)) ||
            export.Lines is null or { Count: < 1 or > 500 })
            return Invalid("REMITO_DESTINATION_OR_SCHEMA_MISMATCH");
        var active = products.Where(item => item.CompanyId == companyId && item.IsActive)
            .ToDictionary(item => item.NormalizedCode, StringComparer.Ordinal);
        InventoryTransferReceipt receipt;
        try
        {
            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
                .ToLowerInvariant();
            receipt = new InventoryTransferReceipt(companyId, destinationBranchId, operationId,
                companyCode, export.OriginBranchName!, export.DocumentNumber!,
                export.DocumentDate, actorId, now, export.OriginBranchId,
                export.TransferId, sha256);
        }
        catch (ArgumentException) { return Invalid("INVALID_REMITO"); }
        var sourceLines = new HashSet<Guid>();
        var sourcePieces = new HashSet<Guid>();
        var lines = new List<InventoryTransferReceiptLine>(export.Lines.Count);
        foreach (var input in export.Lines)
        {
            if (input.LineId == Guid.Empty || !sourceLines.Add(input.LineId) ||
                string.IsNullOrWhiteSpace(input.ProductCode) ||
                !active.TryGetValue(input.ProductCode.Trim().ToUpperInvariant(),
                    out var product) ||
                input.Quantity <= 0 ||
                (input.OriginPieceId.HasValue &&
                    !sourcePieces.Add(input.OriginPieceId.Value)) ||
                !string.Equals(input.Unit, product.Unit, StringComparison.Ordinal) ||
                !string.Equals(input.SaleMode,
                    product.SaleMode == ProductSaleMode.Unit ? "unit" : "weight",
                    StringComparison.Ordinal))
                return Invalid("REMITO_LINE_NOT_AVAILABLE");
            var isPiece = input.OriginPieceId.HasValue ||
                !string.IsNullOrWhiteSpace(input.ExternalIdentifier);
            if (isPiece && (product.SaleMode != ProductSaleMode.Weight ||
                string.IsNullOrWhiteSpace(input.SourceSystem) ||
                string.IsNullOrWhiteSpace(input.ExternalIdentifier) ||
                string.IsNullOrWhiteSpace(input.RawBarcode)))
                return Invalid("REMITO_PIECE_INVALID");
            try
            {
                lines.Add(new InventoryTransferReceiptLine(receipt.Id, product,
                    input.Quantity, 0, isPiece, input.LineId, input.OriginPieceId,
                    input.SourceSystem, input.ExternalIdentifier, input.RawBarcode));
            }
            catch (ArgumentException) { return Invalid("REMITO_LINE_NOT_AVAILABLE"); }
        }
        receipt.ReplaceLines(lines);
        return new TransferRemitoImportPlan(receipt, lines, null);
    }

    private static TransferRemitoImportPlan Invalid(string code) => new(null, [], code);

    private sealed record TransferExport(int SchemaVersion, Guid TransferId,
        Guid OperationId, string? DocumentNumber, DateOnly DocumentDate,
        DateTimeOffset DispatchedAtUtc, Guid CompanyId, string? CompanyCode,
        string? CompanyName, Guid OriginBranchId, string? OriginBranchName,
        Guid? DestinationBranchId, string? DestinationName,
        IReadOnlyList<TransferExportLine>? Lines);

    private sealed record TransferExportLine(Guid LineId, Guid ProductId,
        string? ProductCode, string? ProductName, string? Unit, string? SaleMode,
        decimal Quantity, Guid? OriginPieceId, string? SourceSystem,
        string? IdentifierField, string? ExternalIdentifier, string? RawBarcode);
}
