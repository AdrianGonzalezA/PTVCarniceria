using Carnicerias.Infrastructure;

namespace Carnicerias.Api.History;

internal static class StockMovementKindCode
{
    private const string SaleReasonPrefix = "Egreso por venta ";

    internal static bool TryParse(string? code, out InventoryMovementKind? kind)
    {
        kind = code switch
        {
            null or "" => null,
            "openingBalance" => InventoryMovementKind.OpeningBalance,
            "adjustment" => InventoryMovementKind.Adjustment,
            "sale" => InventoryMovementKind.Sale,
            "pieceReceipt" => InventoryMovementKind.PieceReceipt,
            "receipt" => InventoryMovementKind.Receipt,
            "transferOut" => InventoryMovementKind.TransferOut,
            "transferIn" => InventoryMovementKind.TransferIn,
            "physicalCountAdjustment" => InventoryMovementKind.PhysicalCountAdjustment,
            _ => null
        };
        return string.IsNullOrEmpty(code) || kind.HasValue;
    }

    internal static string Format(InventoryMovementKind kind) => kind switch
    {
        InventoryMovementKind.OpeningBalance => "openingBalance",
        InventoryMovementKind.Adjustment => "adjustment",
        InventoryMovementKind.Sale => "sale",
        InventoryMovementKind.PieceReceipt => "pieceReceipt",
        InventoryMovementKind.Receipt => "receipt",
        InventoryMovementKind.TransferOut => "transferOut",
        InventoryMovementKind.TransferIn => "transferIn",
        InventoryMovementKind.PhysicalCountAdjustment => "physicalCountAdjustment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static Guid? SaleId(InventoryMovementKind kind, string reason) =>
        kind == InventoryMovementKind.Sale && reason.StartsWith(SaleReasonPrefix,
            StringComparison.Ordinal) &&
        Guid.TryParseExact(reason.AsSpan(SaleReasonPrefix.Length), "N", out var saleId)
            ? saleId : null;

    internal static string? SaleNumber(InventoryMovementKind kind, string reason) =>
        SaleId(kind, reason) is { } saleId ? saleId.ToString("N") : null;

    internal static string SearchPattern(string term) => "%" + term
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
