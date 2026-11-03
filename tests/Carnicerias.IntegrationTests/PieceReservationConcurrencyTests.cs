using System.Data;
using Carnicerias.Api.Inventory;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class PieceReservationConcurrencyTests
{
    [I02ApprovedVisualWriteFact]
    [Trait("Category", "I02ApprovedDatabaseWrite")]
    public async Task TwoBoxesCannotReserveOrSellTheSamePiece()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I02 fixture {suffix}", $"I02-{suffix}");
        var branch = new Branch(company.Id, "Sucursal de concurrencia");
        var firstCashier = UserIdentity.CreateForCompany(company.Id, $"i02a-{suffix}",
            $"i02a-{suffix}@example.test", "test-only-hash");
        var secondCashier = UserIdentity.CreateForCompany(company.Id, $"i02b-{suffix}",
            $"i02b-{suffix}@example.test", "test-only-hash");
        var category = new ProductCategory(company.Id, "Trazables");
        var product = new CatalogProduct(company.Id, category.Id, "I02-PIECE", "Pieza de prueba",
            "kg", ProductSaleMode.Weight, 100m);
        var priceList = new PriceList(company.Id, "Prueba");
        var profile = new BarcodeProfile(company.Id, "I02", 1, "pro_identif(6) peso(5)",
            "peso", 3, DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var movement = new InventoryMovement(company.Id, branch.Id, product.Id, firstCashier.Id,
            Guid.NewGuid(), InventoryMovementKind.PieceReceipt, 1.000m,
            "Fixture I02", now);
        var piece = new InventoryPiece(company.Id, branch.Id, product.Id, profile.Id,
            firstCashier.Id, Guid.NewGuid(), movement.Id, "I02 fixture", suffix,
            "25066101000", 1.000m, now);
        var balance = new BranchInventoryBalance(company.Id, branch.Id, product.Id);
        balance.SetQuantities(1.000m, 0m);
        var firstDraft = new SaleDraft(company.Id, branch.Id, firstCashier.Id, priceList.Id, now);
        var secondDraft = new SaleDraft(company.Id, branch.Id, secondCashier.Id, priceList.Id, now);
        var seeded = false;

        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.AddRange(company, branch, firstCashier, secondCashier, category, product,
                    priceList, profile, movement, piece, balance, firstDraft, secondDraft);
                await db.SaveChangesAsync();
                // Also clean up if the commit response is lost after PostgreSQL committed.
                seeded = true;
                await transaction.CommitAsync();
            }

            using var barrier = new Barrier(2);
            async Task<bool> ClaimAsync(Guid draftId, Guid cashierId)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                var current = await db.InventoryPieces.AsNoTracking().SingleAsync(item =>
                    item.Id == piece.Id);
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(20)));
                try
                {
                    var claimed = await PieceReservationTransactions.ReserveAsync(db, current,
                        draftId, cashierId, now.AddSeconds(1), CancellationToken.None);
                    if (claimed)
                    {
                        await db.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    else await transaction.RollbackAsync();
                    return claimed;
                }
                catch (Exception exception) when (PieceReservationTransactions.IsConcurrencyConflict(exception))
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            }

            var results = await Task.WhenAll(
                Task.Run(() => ClaimAsync(firstDraft.Id, firstCashier.Id)),
                Task.Run(() => ClaimAsync(secondDraft.Id, secondCashier.Id)));
            Assert.Single(results, won => won);
            var winningDraft = results[0] ? firstDraft : secondDraft;
            var winningCashier = results[0] ? firstCashier : secondCashier;
            var losingDraft = results[0] ? secondDraft : firstDraft;
            var losingCashier = results[0] ? secondCashier : firstCashier;

            await using (var recovered = new PlatformAccessDbContext(options))
            {
                var persisted = await recovered.InventoryPieces.AsNoTracking()
                    .SingleAsync(item => item.Id == piece.Id);
                Assert.Equal(InventoryPieceStatus.Reserved, persisted.Status);
                Assert.Equal(winningDraft.Id, persisted.ReservedBySaleDraftId);
                Assert.Single(await recovered.InventoryPieceStateEvents.AsNoTracking()
                    .Where(item => item.PieceId == piece.Id).ToArrayAsync());
                Assert.False(await PieceReservationTransactions.ReserveAsync(recovered, persisted,
                    losingDraft.Id, losingCashier.Id, now.AddSeconds(2), CancellationToken.None));
            }

            await using (var released = new PlatformAccessDbContext(options))
            {
                await using var transaction = await released.Database.BeginTransactionAsync();
                var persisted = await released.InventoryPieces.AsNoTracking()
                    .SingleAsync(item => item.Id == piece.Id);
                Assert.True(await PieceReservationTransactions.ReleaseAsync(released, persisted,
                    winningDraft.Id, winningCashier.Id, InventoryPieceEventKind.SaleReleased,
                    "Cancelación de prueba", now.AddSeconds(3), CancellationToken.None));
                await released.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await using (var sold = new PlatformAccessDbContext(options))
            {
                await using var transaction = await sold.Database.BeginTransactionAsync();
                var available = await sold.InventoryPieces.AsNoTracking()
                    .SingleAsync(item => item.Id == piece.Id);
                Assert.True(await PieceReservationTransactions.ReserveAsync(sold, available,
                    losingDraft.Id, losingCashier.Id, now.AddSeconds(4), CancellationToken.None));
                Assert.True(await PieceReservationTransactions.SellAsync(sold, available,
                    losingDraft.Id, losingCashier.Id, now.AddSeconds(5), CancellationToken.None));
                await sold.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await using (var verified = new PlatformAccessDbContext(options))
            {
                var persisted = await verified.InventoryPieces.AsNoTracking()
                    .SingleAsync(item => item.Id == piece.Id);
                Assert.Equal(InventoryPieceStatus.Sold, persisted.Status);
                Assert.Null(persisted.ReservedBySaleDraftId);
                Assert.False(await PieceReservationTransactions.ReserveAsync(verified, persisted,
                    winningDraft.Id, winningCashier.Id, now.AddSeconds(6), CancellationToken.None));
                Assert.Equal(4, await verified.InventoryPieceStateEvents.AsNoTracking()
                    .CountAsync(item => item.PieceId == piece.Id));
            }

            // The stock write lease is scoped to this article and branch and expires without
            // any cleanup job. A different connection cannot update the held stock.
            var lockId = Guid.NewGuid();
            await using (var owner = new PlatformAccessDbContext(options))
            {
                Assert.Equal(1, await owner.BranchInventoryBalances.Where(item =>
                    item.CompanyId == company.Id && item.BranchId == branch.Id &&
                    item.ProductId == product.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ReconciliationLockId, (Guid?)lockId)
                    .SetProperty(item => item.ReconciliationLockPieceId, (Guid?)piece.Id)
                    .SetProperty(item => item.ReconciliationLockOwnerUserId, (Guid?)firstCashier.Id)
                    .SetProperty(item => item.ReconciliationLockStartedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(item => item.ReconciliationLockExpiresAtUtc,
                        (DateTimeOffset?)now.AddMinutes(3))));
            }
            await using (var otherBox = new PlatformAccessDbContext(options))
            {
                await using var transaction = await otherBox.Database.BeginTransactionAsync();
                Assert.True(await StockReconciliationLease.IsLockedForUpdateAsync(otherBox,
                    company.Id, branch.Id, product.Id, now.AddMinutes(1), CancellationToken.None));
                var updated = await StockReconciliationLease.Unlocked(
                    otherBox.BranchInventoryBalances.Where(item =>
                        item.CompanyId == company.Id && item.BranchId == branch.Id &&
                        item.ProductId == product.Id), now.AddMinutes(1))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Reserved, 0.1m));
                Assert.Equal(0, updated);
                Assert.False(await StockReconciliationLease.IsLockedForUpdateAsync(otherBox,
                    company.Id, branch.Id, product.Id, now.AddMinutes(4), CancellationToken.None));
                await transaction.RollbackAsync();
            }
        }
        finally
        {
            if (seeded) await RemoveFixtureAsync(options, company.Id);
        }
    }

    private static async Task RemoveFixtureAsync(DbContextOptions<PlatformAccessDbContext> options,
        Guid companyId)
    {
        await using var db = new PlatformAccessDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.InventoryPieceStateEvents.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryPieces.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.SaleDrafts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.InventoryMovements.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BranchInventoryBalances.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.BarcodeProfiles.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.CatalogProducts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.ProductCategories.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.PriceLists.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Users.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Branches.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync();
        await db.Companies.Where(item => item.Id == companyId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
}

public sealed class I02ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I02ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I02_WRITES_APPROVED") != "I02_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Requires specific PO authorization for temporary writes in the single visual database.";
    }
}
