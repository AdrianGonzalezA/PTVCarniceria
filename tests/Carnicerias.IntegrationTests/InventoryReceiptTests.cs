using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryReceiptTests
{
    [Fact]
    public void PackageReadsPreserveFactorsAndSumWithLooseUnits()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var product = Product(company, ProductSaleMode.Unit);
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(),
            new Supplier(company, "F1", "Proveedor"), "remito", "B-1",
            new DateOnly(2026, 11, 6), actor, DateTimeOffset.UtcNow);
        var six = new ProductCode(company, product.Id, "CAJA6", ProductCodeKind.PurchasePackage, 6);
        var twelve = new ProductCode(company, product.Id, "CAJA12", ProductCodeKind.PurchasePackage, 12);

        AddPackage(six, 2);
        six.ChangePackageFactor(12);
        AddPackage(twelve, 1);
        var loose = new InventoryReceiptLine(receipt.Id, product, 2);
        receipt.ReplaceLines([.. receipt.Lines, loose]);

        Assert.Equal(26, receipt.Lines.Sum(line => line.ReceivedQuantity));
        Assert.Equal([6, 12], receipt.PackageReads.Select(read => read.UnitsPerPackage));
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([loose]));
        receipt.Approve(actor, DateTimeOffset.UtcNow);
        Assert.Equal(InventoryReceiptStatus.Approved, receipt.Status);

        void AddPackage(ProductCode code, int count)
        {
            var line = new InventoryReceiptLine(receipt.Id, product,
                count * code.UnitsPerPackage!.Value);
            var read = new InventoryReceiptPackageRead(company, receipt.Id, line.Id,
                Guid.NewGuid(), code, count, actor, DateTimeOffset.UtcNow);
            receipt.AddPackageRead(line, read);
        }
    }

    [Fact]
    public void TraceableReadsRemainIndividualAndCannotBeRemovedByGenericEdits()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var product = Product(company, ProductSaleMode.Weight);
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(),
            new Supplier(company, "F1", "Proveedor"), "remito", "R-piezas",
            new DateOnly(2026, 11, 5), actor, DateTimeOffset.UtcNow);
        var firstLine = new InventoryReceiptLine(receipt.Id, product, 51.600m);
        var first = new InventoryReceiptPieceRead(company, receipt.Id, firstLine.Id,
            Guid.NewGuid(), Guid.NewGuid(), "Ciclo 2", "pro_identif", "250661",
            "25066151600", 51.600m);
        receipt.AddPieceRead(firstLine, first);
        var secondLine = new InventoryReceiptLine(receipt.Id, product, 50.400m);
        var second = new InventoryReceiptPieceRead(company, receipt.Id, secondLine.Id,
            Guid.NewGuid(), Guid.NewGuid(), "Ciclo 2", "pro_identif", "250656",
            "25065650400", 50.400m);
        receipt.AddPieceRead(secondLine, second);
        Assert.Equal(102m, receipt.Lines.Sum(line => line.ReceivedQuantity));
        Assert.Equal(2, receipt.PieceReads.Count);
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([firstLine]));
        var duplicateLine = new InventoryReceiptLine(receipt.Id, product, 51.600m);
        var duplicate = new InventoryReceiptPieceRead(company, receipt.Id, duplicateLine.Id,
            Guid.NewGuid(), first.BarcodeProfileId, "CICLO 2", "pro_identif", "250661",
            "25066151600", 51.600m);
        Assert.Throws<InvalidOperationException>(() => receipt.AddPieceRead(duplicateLine, duplicate));
        var genericLine = new InventoryReceiptLine(receipt.Id, product, 1.250m);
        receipt.ReplaceLines([firstLine, secondLine, genericLine]);
        Assert.Equal(3, receipt.Lines.Count);
        Assert.Equal(2, receipt.PieceReads.Count);
        receipt.Approve(actor, DateTimeOffset.UtcNow);
        first.LinkPiece(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => first.LinkPiece(Guid.NewGuid()));
    }

    [Fact]
    public void ApprovesOnlyObservedQuantityAndLeavesDifferenceForAuditedResolution()
    {
        var company = Guid.NewGuid();
        var supplier = new Supplier(company, "F1", "Frigorífico Uno", "30710106513");
        var product = Product(company, ProductSaleMode.Unit);
        var actor = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(), supplier,
            "remito", "R-25", new DateOnly(2026, 11, 5), actor, now);
        var line = new InventoryReceiptLine(receipt.Id, product, 8, 10);
        receipt.AddLine(line);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<InventoryReceiptLine>)receipt.Lines).Add(
                new InventoryReceiptLine(receipt.Id, product, 1)));
        Assert.Throws<InvalidOperationException>(() => receipt.LinkMovement(line.Id, Guid.NewGuid()));
        supplier.UpdateDetails("F1", "Nombre nuevo", null);

        receipt.Approve(actor, now.AddMinutes(1));
        var movementId = Guid.NewGuid();
        var versionBeforeMovement = receipt.Version;
        receipt.LinkMovement(line.Id, movementId);

        Assert.Equal(InventoryReceiptStatus.Approved, receipt.Status);
        Assert.Equal("Frigorífico Uno", receipt.SupplierName);
        Assert.Equal("30710106513", receipt.SupplierIdentification);
        Assert.Equal(8, line.ReceivedQuantity);
        Assert.Equal(movementId, line.InventoryMovementId);
        Assert.Equal(versionBeforeMovement + 1, receipt.Version);
        Assert.Throws<InvalidOperationException>(() => receipt.LinkMovement(line.Id, Guid.NewGuid()));
        Assert.Equal(10, line.ExpectedQuantity);
        Assert.Equal(InventoryReceiptDifferenceStatus.PendingReview, line.DifferenceStatus);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve(actor, now));
        var administrator = Guid.NewGuid();
        receipt.ResolveDifference(line.Id, administrator, "Se entregaron dos unidades menos", now.AddMinutes(2));
        Assert.Equal(InventoryReceiptDifferenceStatus.Resolved, line.DifferenceStatus);
        Assert.Equal(administrator, line.DifferenceResolvedByUserId);
        Assert.Equal(8, line.ReceivedQuantity);
    }

    [Fact]
    public void CancellationNeverApprovesOrCreatesAStockMovement()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(),
            new Supplier(company, "F1", "Proveedor"), "factura", "A-1",
            new DateOnly(2026, 11, 5), actor, DateTimeOffset.UtcNow);
        receipt.AddLine(new InventoryReceiptLine(receipt.Id, Product(company, ProductSaleMode.Weight),
            1.250m));
        receipt.Cancel(actor, DateTimeOffset.UtcNow);

        Assert.Equal(InventoryReceiptStatus.Canceled, receipt.Status);
        Assert.Null(Assert.Single(receipt.Lines).InventoryMovementId);
        Assert.Throws<InvalidOperationException>(() => receipt.Approve(actor, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DraftLinesCanBeCorrectedBeforeApprovalButNotAfterward()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var product = Product(company, ProductSaleMode.Unit);
        var receipt = new InventoryReceipt(company, Guid.NewGuid(), Guid.NewGuid(),
            new Supplier(company, "F1", "Proveedor"), "remito", "R-1",
            new DateOnly(2026, 11, 5), actor, DateTimeOffset.UtcNow);
        receipt.AddLine(new InventoryReceiptLine(receipt.Id, product, 3));
        var version = receipt.Version;
        var corrected = new InventoryReceiptLine(receipt.Id, product, 2, 3);

        receipt.ReplaceLines([corrected]);

        Assert.Single(receipt.Lines);
        Assert.Same(corrected, receipt.Lines[0]);
        Assert.Equal(version + 1, receipt.Version);
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([
            new InventoryReceiptLine(Guid.NewGuid(), product, 1)]));
        receipt.Approve(actor, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => receipt.ReplaceLines([]));
    }

    [Fact]
    public void LegacyPieceHasAuditedSourceButNoInventedSupplier()
    {
        var company = Guid.NewGuid();
        var receipt = InventoryReceipt.LegacyPiece(company, Guid.NewGuid(), Guid.NewGuid(),
            "Frigorífico ciclo 2", Product(company, ProductSaleMode.Weight), 1.200m,
            Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(InventoryReceiptStatus.Approved, receipt.Status);
        Assert.Equal("legacyPiece", receipt.DocumentType);
        Assert.Null(receipt.SupplierId);
        Assert.Equal("Frigorífico ciclo 2", receipt.SourceSystem);
        Assert.Equal(1.200m, Assert.Single(receipt.Lines).ReceivedQuantity);
    }

    [Fact]
    public void RejectsOtherCompanyOrFractionalUnitQuantity()
    {
        var company = Guid.NewGuid();
        var foreign = new Supplier(Guid.NewGuid(), "F", "Foreign");
        Assert.Throws<ArgumentException>(() => new InventoryReceipt(company, Guid.NewGuid(),
            Guid.NewGuid(), foreign, "remito", "R-1", new DateOnly(2026, 11, 5),
            Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new InventoryReceiptLine(Guid.NewGuid(),
            Product(company, ProductSaleMode.Unit), 1.5m));
    }

    [Fact]
    public void ModelKeepsReceiptAndMovementIdentityUniquePerCompanyAndBranch()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=design_only").Options;
        using var db = new PlatformAccessDbContext(options);
        var receipt = db.Model.FindEntityType(typeof(InventoryReceipt));
        var line = db.Model.FindEntityType(typeof(InventoryReceiptLine));

        Assert.NotNull(receipt);
        Assert.NotNull(line);
        Assert.Equal("receipt_documents", receipt.GetTableName());
        Assert.Equal("inventory", receipt.GetSchema());
        Assert.Equal("receipt_lines", line.GetTableName());
        Assert.Contains(receipt.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(InventoryReceipt.CompanyId), nameof(InventoryReceipt.BranchId),
                    nameof(InventoryReceipt.OperationId)]));
        Assert.Contains(line.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(InventoryReceiptLine.InventoryMovementId)]));
    }

    private static CatalogProduct Product(Guid companyId, ProductSaleMode mode) =>
        new(companyId, Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Producto", mode ==
            ProductSaleMode.Unit ? "un" : "kg", mode, null);
}
