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

    [Fact]
    public void ReservationBelongsToOneDraftAndSurvivesAnUnchangedSave()
    {
        var piece = NewPiece();
        var firstDraft = Guid.NewGuid();
        var reservedAt = DateTimeOffset.UtcNow;

        Assert.Equal(InventoryPieceStatus.Available, piece.Status);
        piece.ReserveForSale(firstDraft, reservedAt);
        piece.ReserveForSale(firstDraft, reservedAt.AddMinutes(1));

        Assert.Equal(InventoryPieceStatus.Reserved, piece.Status);
        Assert.Equal(firstDraft, piece.ReservedBySaleDraftId);
        Assert.Equal(reservedAt, piece.ReservedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            piece.ReserveForSale(Guid.NewGuid(), reservedAt.AddMinutes(2)));
    }

    [Fact]
    public void CancelledReservationCanBeAcquiredByAnotherDraft()
    {
        var piece = NewPiece();
        var firstDraft = Guid.NewGuid();
        var secondDraft = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        piece.ReserveForSale(firstDraft, now);

        Assert.Throws<InvalidOperationException>(() => piece.ReleaseSaleReservation(secondDraft, now));
        piece.ReleaseSaleReservation(firstDraft, now.AddMinutes(1));
        piece.ReserveForSale(secondDraft, now.AddMinutes(2));

        Assert.Equal(InventoryPieceStatus.Reserved, piece.Status);
        Assert.Equal(secondDraft, piece.ReservedBySaleDraftId);
    }

    [Fact]
    public void SoldTransferredAndConsumedPiecesCannotReturnToSale()
    {
        var now = DateTimeOffset.UtcNow;
        var draftId = Guid.NewGuid();
        var sold = NewPiece();
        sold.ReserveForSale(draftId, now);
        Assert.Throws<InvalidOperationException>(() => sold.MarkSold(Guid.NewGuid(), now));
        sold.MarkSold(draftId, now.AddMinutes(1));
        Assert.Equal(InventoryPieceStatus.Sold, sold.Status);
        Assert.Null(sold.ReservedBySaleDraftId);
        Assert.Throws<InvalidOperationException>(() => sold.ReserveForSale(Guid.NewGuid(), now));

        var transferred = NewPiece();
        transferred.MarkTransferred(now);
        Assert.Throws<InvalidOperationException>(() => transferred.ReserveForSale(draftId, now));

        var consumed = NewPiece();
        consumed.MarkConsumed(now);
        Assert.Throws<InvalidOperationException>(() => consumed.ReserveForSale(draftId, now));
    }

    [Fact]
    public void DatabaseModelKeepsReservationOwnerAndAuditInTheSameCompany()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres").Options;
        using var db = new PlatformAccessDbContext(options);
        var piece = db.Model.FindEntityType(typeof(InventoryPiece));
        var stateEvent = db.Model.FindEntityType(typeof(InventoryPieceStateEvent));

        Assert.NotNull(piece);
        Assert.NotNull(stateEvent);
        Assert.Equal("inventory", stateEvent.GetSchema());
        Assert.Contains(piece.GetForeignKeys(), key =>
            key.Properties.Select(property => property.Name).SequenceEqual([
                nameof(InventoryPiece.CompanyId), nameof(InventoryPiece.ReservedBySaleDraftId)]) &&
            key.PrincipalEntityType.ClrType == typeof(SaleDraft));
        Assert.Contains(stateEvent.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(InventoryPiece));
    }

    [Fact]
    public void AdminReleaseEventPreservesActorReasonAndPreviousOwner()
    {
        var companyId = Guid.NewGuid();
        var pieceId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var item = new InventoryPieceStateEvent(companyId, Guid.NewGuid(), pieceId,
            InventoryPieceStatus.Reserved, InventoryPieceStatus.Available,
            InventoryPieceEventKind.AdminReleased, draftId, adminId,
            "  Se recuperó la caja después de una caída  ", DateTimeOffset.UtcNow);

        Assert.Equal(adminId, item.ActorUserId);
        Assert.Equal(draftId, item.SaleDraftId);
        Assert.Equal("Se recuperó la caja después de una caída", item.Reason);
        Assert.Equal(InventoryPieceStatus.Available, item.Status);
    }

    private static InventoryPiece NewPiece() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "Frigorífico ciclo 2", Guid.NewGuid().ToString("N"),
        "2250661516008", 51.600m, DateTimeOffset.UtcNow);
}
