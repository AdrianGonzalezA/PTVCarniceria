namespace Carnicerias.Infrastructure;

/// <summary>A package count captured before a receipt changes stock.</summary>
public sealed class InventoryReceiptPackageRead
{
    private InventoryReceiptPackageRead() => Code = string.Empty;

    public InventoryReceiptPackageRead(Guid companyId, Guid receiptId, Guid lineId,
        Guid operationId, ProductCode code, int packageCount, Guid userId, DateTimeOffset readAtUtc)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (companyId == Guid.Empty || receiptId == Guid.Empty || lineId == Guid.Empty ||
            operationId == Guid.Empty || userId == Guid.Empty ||
            code.CompanyId != companyId || !code.IsActive ||
            code.Kind != ProductCodeKind.PurchasePackage ||
            code.UnitsPerPackage is not int factor ||
            packageCount is < 1 or > 100_000 ||
            (long)packageCount * factor > 100_000)
            throw new ArgumentException("An active package code and valid count are required.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        ReceiptId = receiptId;
        LineId = lineId;
        OperationId = operationId;
        ProductId = code.ProductId;
        Code = code.Code;
        UnitsPerPackage = factor;
        PackageCount = packageCount;
        UnitQuantity = packageCount * factor;
        ReadByUserId = userId;
        ReadAtUtc = readAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid LineId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Code { get; private set; }
    public int UnitsPerPackage { get; private set; }
    public int PackageCount { get; private set; }
    public int UnitQuantity { get; private set; }
    public Guid ReadByUserId { get; private set; }
    public DateTimeOffset ReadAtUtc { get; private set; }
}
