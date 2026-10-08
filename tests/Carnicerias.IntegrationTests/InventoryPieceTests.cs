using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryPieceTests
{
    [Fact]
    public void DraftKeepsTwoPiecesOfTheSameProductAsSeparateLines()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var draft = new SaleDraft(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        draft.ReplaceLines([
            new SaleDraftLine(companyId, productId, "1002", "Asado", "kg", ProductSaleMode.Weight,
                0.500m, 11500m, firstId, "999001"),
            new SaleDraftLine(companyId, productId, "1002", "Asado", "kg", ProductSaleMode.Weight,
                0.750m, 11500m, secondId, "999002")
        ], DateTimeOffset.UtcNow);

        Assert.Equal(2, draft.Lines.Count);
        Assert.Contains(draft.Lines, line => line.InventoryPieceId == firstId && line.Quantity == 0.500m);
        Assert.Contains(draft.Lines, line => line.InventoryPieceId == secondId && line.Quantity == 0.750m);
        var retainedId = draft.Lines.Single(line => line.InventoryPieceId == firstId).Id;
        draft.ReplaceLines([
            new SaleDraftLine(companyId, productId, "1002", "Asado", "kg", ProductSaleMode.Weight,
                0.500m, 11500m, firstId, "999001")
        ], DateTimeOffset.UtcNow);
        Assert.Single(draft.Lines);
        Assert.Equal(retainedId, draft.Lines[0].Id);
    }

    [Fact]
    public void ConfirmedLineModelPreventsSellingTheSamePieceTwice()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres").Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(ConfirmedSaleLine));

        Assert.NotNull(entity);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ConfirmedSaleLine.InventoryPieceId)]));
    }

    [Fact]
    public void ReceiptKeepsPieceIdentitySeparateFromCatalogProductAndWeight()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var first = new InventoryPiece(companyId, branchId, productId, profileId, userId,
            Guid.NewGuid(), Guid.NewGuid(), "Frigorífico ciclo 2", "250661", "2250661516008",
            51.600m, DateTimeOffset.UtcNow);
        var second = new InventoryPiece(companyId, branchId, productId, profileId, userId,
            Guid.NewGuid(), Guid.NewGuid(), "Frigorífico ciclo 2", "250656", "2250656504000",
            51.600m, DateTimeOffset.UtcNow);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(productId, first.ProductId);
        Assert.Equal("250661", first.ExternalIdentifier);
        Assert.Equal(51.600m, first.ReceivedWeightKg);
        Assert.Equal("FRIGORÍFICO CICLO 2", first.NormalizedSourceSystem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.2345)]
    public void ReceiptRejectsInvalidWeight(decimal weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryPiece(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "Origen", "250661", "2250661516008", weight,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ReceiptRejectsSourceThatExceedsLimitAfterUnicodeNormalization()
    {
        Assert.Throws<ArgumentException>(() => new InventoryPiece(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), new string('\uFB00', 100), "250661", "2250661516008",
            51.600m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DatabaseModelProtectsExternalIdentityAndOperationWithinScope()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres").Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(InventoryPiece));

        Assert.NotNull(entity);
        Assert.Equal("inventory", entity.GetSchema());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(InventoryPiece.CompanyId), nameof(InventoryPiece.NormalizedSourceSystem),
                nameof(InventoryPiece.ExternalIdentifier)]));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(InventoryPiece.CompanyId), nameof(InventoryPiece.BranchId),
                nameof(InventoryPiece.OperationId)]));
    }
}
