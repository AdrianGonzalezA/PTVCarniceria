using Carnicerias.Api.ExcelImport;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class StockImportVisualDatabaseTests
{
    [VisualPostgreSqlFact]
    [Trait("Category", "VisualDatabaseIntegration")]
    public async Task AppliesOneVariationAndMovementThenRollsBack()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var db = new PlatformAccessDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var product = await db.CatalogProducts.AsNoTracking()
            .Where(item => item.IsActive).FirstAsync();
        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(item => item.CompanyId == product.CompanyId && item.IsActive);
        var userId = await db.Users.AsNoTracking().Select(user => user.Id).FirstAsync();
        var before = await db.BranchInventoryBalances.AsNoTracking().SingleOrDefaultAsync(balance =>
            balance.CompanyId == product.CompanyId && balance.BranchId == branch.Id &&
            balance.ProductId == product.Id);
        var delta = product.SaleMode == ProductSaleMode.Unit ? 1m : 0.125m;
        var marker = Guid.NewGuid().ToString("N");
        var parsed = new ParsedImport([
            new ImportRow("Existencias", 2, new Dictionary<string, ImportCell>
            {
                ["CodigoArticulo"] = new(product.Code, null),
                ["Variacion"] = new(null, delta),
                ["Descripcion"] = new(marker, null)
            })
        ], []);

        var preview = await AdminExcelImportProcessor.AssessAsync(db, product.CompanyId, branch.Id,
            userId, "stock", parsed, DateTimeOffset.UtcNow, false, CancellationToken.None);
        Assert.True(preview.CanApply);
        var applied = await AdminExcelImportProcessor.AssessAsync(db, product.CompanyId, branch.Id,
            userId, "stock", parsed, DateTimeOffset.UtcNow, true, CancellationToken.None);
        Assert.True(applied.CanApply);
        await db.SaveChangesAsync();

        var after = await db.BranchInventoryBalances.AsNoTracking().SingleAsync(balance =>
            balance.CompanyId == product.CompanyId && balance.BranchId == branch.Id &&
            balance.ProductId == product.Id);
        Assert.Equal((before?.OnHand ?? 0m) + delta, after.OnHand);
        Assert.Equal(before?.Reserved ?? 0m, after.Reserved);
        Assert.Equal(delta, await db.InventoryMovements.AsNoTracking().Where(movement =>
            movement.CompanyId == product.CompanyId && movement.BranchId == branch.Id &&
            movement.ProductId == product.Id && movement.Reason == $"Importación Excel: {marker}")
            .Select(movement => movement.QuantityDelta).SingleAsync());

        await transaction.RollbackAsync();
        Assert.False(await db.InventoryMovements.AsNoTracking().AnyAsync(movement =>
            movement.Reason == $"Importación Excel: {marker}"));
    }
}

public sealed class VisualPostgreSqlFactAttribute : FactAttribute
{
    public VisualPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Set CARNICERIAS_VISUAL_CONNECTION_STRING to the existing local carnicerias_test_visual database.";
    }
}
