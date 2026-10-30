using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class SalePieceCountTests
{
    [Fact]
    public void DraftKeepsPhysicalPiecesSeparateFromWeightAndUpdatesThem()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var draft = new SaleDraft(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        var first = new SaleDraftLine(companyId, productId, "2546", "Cortito", "kg",
            ProductSaleMode.Weight, 51.6m, 100m, pieceCount: 1);
        draft.ReplaceLines([first], DateTimeOffset.UtcNow);

        Assert.Equal(51.6m, draft.Lines.Single().Quantity);
        Assert.Equal(1, draft.Lines.Single().PieceCount);

        var updated = new SaleDraftLine(companyId, productId, "2546", "Cortito", "kg",
            ProductSaleMode.Weight, 52.1m, 100m, pieceCount: 2);
        draft.ReplaceLines([updated], DateTimeOffset.UtcNow);

        Assert.Equal(first.Id, draft.Lines.Single().Id);
        Assert.Equal(52.1m, draft.Lines.Single().Quantity);
        Assert.Equal(2, draft.Lines.Single().PieceCount);
    }

    [Fact]
    public void RejectsInvalidPhysicalPieceCounts()
    {
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaleDraftLine(companyId, productId,
            "2546", "Cortito", "kg", ProductSaleMode.Weight, 1m, 100m, pieceCount: 0));
        Assert.Throws<ArgumentException>(() => new SaleDraftLine(companyId, productId,
            "8001", "Bolsa", "unidad", ProductSaleMode.Unit, 1m, 100m, pieceCount: 1));
        Assert.Throws<ArgumentException>(() => new ConfirmedSaleLine(companyId, productId,
            "2546", "Cortito", "kg", ProductSaleMode.Weight, 1m, 100m,
            Guid.NewGuid(), "250661", 2));
    }
}
