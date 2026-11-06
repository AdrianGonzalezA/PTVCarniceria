using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class PhysicalStockCountTests
{
    [Fact]
    public void CountDocumentsRequireSubmissionAndKeepDecisionAudit()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var counter = Guid.NewGuid();
        var administrator = Guid.NewGuid();
        var product = new CatalogProduct(company, Guid.NewGuid(), "A1",
            "Asado", "kg", ProductSaleMode.Weight, null);
        var count = new PhysicalStockCount(company, branch, Guid.NewGuid(),
            counter, DateTimeOffset.UtcNow);
        var line = new PhysicalStockCountLine(count.Id, product,
            StockCountPolicy.TheoreticalStock, 9.5m, 10m, 0, 0, 0, 0,
            Guid.NewGuid(), counter, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => count.Submit(counter,
            DateTimeOffset.UtcNow));
        count.AddLine(line);
        Assert.Throws<InvalidOperationException>(() => count.AddLine(line));
        count.Submit(counter, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => count.AddLine(line));
        count.Approve(administrator, "Merma verificada", DateTimeOffset.UtcNow);
        count.LinkMovement(line.Id, Guid.NewGuid());
        Assert.Equal(PhysicalStockCountStatus.Approved, count.Status);
        Assert.Equal(administrator, count.DecidedByUserId);
        Assert.Equal("Merma verificada", count.DecisionReason);
        Assert.Equal(-0.5m, line.Delta);
        Assert.NotNull(line.InventoryMovementId);
    }

    [Fact]
    public void SnapshotRejectsMovementEvenWhenFinalBalanceMatches()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var product = new CatalogProduct(company, Guid.NewGuid(), "B1",
            "Aguja", "kg", ProductSaleMode.Weight, null);
        var firstMovement = Guid.NewGuid();
        var line = new PhysicalStockCountLine(Guid.NewGuid(), product,
            StockCountPolicy.TheoreticalStock, 10m, 12m, 2m, 4m, 0, 0,
            firstMovement, actor, DateTimeOffset.UtcNow);
        var balance = new BranchInventoryBalance(company, branch, product.Id);
        balance.SetQuantities(12m, 2m, protectedOnHand: 4m);
        Assert.True(line.MatchesCurrent(balance, firstMovement,
            StockCountPolicy.TheoreticalStock));
        Assert.False(line.MatchesCurrent(balance, Guid.NewGuid(),
            StockCountPolicy.TheoreticalStock));
        Assert.False(line.MatchesCurrent(balance, firstMovement,
            StockCountPolicy.MassObservation));
        Assert.True(line.CanApplyStockDifference());
        var protectedShortage = new PhysicalStockCountLine(Guid.NewGuid(), product,
            StockCountPolicy.TheoreticalStock, 4m, 12m, 2m, 4m, 0, 0,
            firstMovement, actor, DateTimeOffset.UtcNow);
        Assert.False(protectedShortage.CanApplyStockDifference());
    }

    [Fact]
    public void MassObservationNeverCreatesTheoreticalAdjustment()
    {
        var company = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var category = new ProductCategory(company, "Cortes");
        category.SetCountPolicy(StockCountPolicy.MassObservation);
        var product = new CatalogProduct(company, category.Id, "C1",
            "Corte comercial", "kg", ProductSaleMode.Weight, null);
        var count = new PhysicalStockCount(company, Guid.NewGuid(), Guid.NewGuid(),
            actor, DateTimeOffset.UtcNow);
        var line = new PhysicalStockCountLine(count.Id, product,
            category.CountPolicy, 8m, 0, 0, 0, 0, 0, null,
            actor, DateTimeOffset.UtcNow);
        count.AddLine(line);
        count.Submit(actor, DateTimeOffset.UtcNow);
        count.Approve(Guid.NewGuid(), "Medición física", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            count.LinkMovement(line.Id, Guid.NewGuid()));
        Assert.Null(line.InventoryMovementId);
        product.SetCountPolicyOverride(StockCountPolicy.TheoreticalStock);
        Assert.Equal(StockCountPolicy.TheoreticalStock, product.CountPolicyOverride);
    }
}
