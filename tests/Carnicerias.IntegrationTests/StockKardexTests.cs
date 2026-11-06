using System.Text.Json;
using Carnicerias.Api.History;
using Carnicerias.Api.Security;
using Carnicerias.Domain.PlatformAccess;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

public sealed class StockMovementKindCodeTests
{
    [Theory]
    [InlineData("openingBalance", InventoryMovementKind.OpeningBalance)]
    [InlineData("adjustment", InventoryMovementKind.Adjustment)]
    [InlineData("sale", InventoryMovementKind.Sale)]
    [InlineData("pieceReceipt", InventoryMovementKind.PieceReceipt)]
    [InlineData("receipt", InventoryMovementKind.Receipt)]
    [InlineData("transferOut", InventoryMovementKind.TransferOut)]
    [InlineData("transferIn", InventoryMovementKind.TransferIn)]
    [InlineData("physicalCountAdjustment", InventoryMovementKind.PhysicalCountAdjustment)]
    public void EveryMovementKindHasItsOwnFilterAndLabel(string code, InventoryMovementKind kind)
    {
        Assert.True(StockMovementKindCode.TryParse(code, out var parsed));
        Assert.Equal(kind, parsed);
        Assert.Equal(code, StockMovementKindCode.Format(kind));
    }

    [Fact]
    public void UnknownKindIsRejectedAndSearchWildcardsAreLiteral()
    {
        Assert.True(StockMovementKindCode.TryParse(null, out var all));
        Assert.Null(all);
        Assert.False(StockMovementKindCode.TryParse("other", out _));
        Assert.Equal("%A\\%B\\_C\\\\D%", StockMovementKindCode.SearchPattern("A%B_C\\D"));
    }

    [Fact]
    public void SaleDocumentIsDerivedOnlyFromTheRecordedSaleReference()
    {
        var saleId = Guid.NewGuid();
        var reason = $"Egreso por venta {saleId:N}";
        Assert.Equal(saleId, StockMovementKindCode.SaleId(InventoryMovementKind.Sale, reason));
        Assert.Equal(saleId.ToString("N"), StockMovementKindCode.SaleNumber(
            InventoryMovementKind.Sale, reason));
        Assert.Null(StockMovementKindCode.SaleId(InventoryMovementKind.Adjustment, reason));
        Assert.Null(StockMovementKindCode.SaleId(InventoryMovementKind.Sale, "Venta sin identificador"));
    }
}

[Collection("DatabaseIntegration")]
public sealed class StockKardexReadOnlyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [VisualReadOnlyFact]
    [Trait("Category", "VisualDatabaseReadOnly")]
    public async Task FiltersAndPagesPreserveTheActualProgressiveBalance()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var db = new PlatformAccessDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var group = await db.InventoryMovements.AsNoTracking()
            .GroupBy(item => new { item.CompanyId, item.BranchId, item.ProductId })
            .Select(items => new { items.Key.CompanyId, items.Key.BranchId,
                items.Key.ProductId, Count = items.Count() })
            .Where(item => item.Count >= 2).OrderByDescending(item => item.Count)
            .FirstAsync();
        var product = await db.CatalogProducts.AsNoTracking()
            .SingleAsync(item => item.CompanyId == group.CompanyId && item.Id == group.ProductId);
        var category = await db.ProductCategories.AsNoTracking()
            .SingleAsync(item => item.CompanyId == group.CompanyId && item.Id == product.CategoryId);
        var accessor = new OperationalContextAccessor();
        accessor.Set(new OperationalContext(Guid.NewGuid(), group.CompanyId, group.BranchId,
            new HashSet<string> { PlatformPermissionCatalog.OrganizationManage }, Guid.NewGuid()), null);

        async Task<JsonElement> Page(int page, string? kind = null,
            DateTimeOffset? from = null, DateTimeOffset? to = null,
            string? productSearch = null, string? categorySearch = null)
        {
            var response = await AdminHistoryEndpoints.ListStockMovementsAsync(db, accessor,
                CancellationToken.None, page, 1, group.BranchId, from, to,
                group.ProductId, product.CategoryId, productSearch, categorySearch, kind);
            return JsonSerializer.SerializeToElement(
                Assert.IsAssignableFrom<IValueHttpResult>(response).Value, JsonOptions);
        }

        var first = (await Page(1)).GetProperty("items")[0];
        var second = (await Page(2)).GetProperty("items")[0];
        var onHand = await db.BranchInventoryBalances.AsNoTracking()
            .Where(item => item.CompanyId == group.CompanyId && item.BranchId == group.BranchId &&
                item.ProductId == group.ProductId)
            .Select(item => item.OnHand).SingleAsync();
        Assert.Equal(onHand, first.GetProperty("runningBalance").GetDecimal());
        var summary = (await Page(1)).GetProperty("summary");
        Assert.Equal(onHand, summary.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(summary.GetProperty("closingBalance").GetDecimal(),
            summary.GetProperty("openingBalance").GetDecimal() +
            summary.GetProperty("openingMovements").GetDecimal() +
            summary.GetProperty("receipts").GetDecimal() +
            summary.GetProperty("pieceReceipts").GetDecimal() +
            summary.GetProperty("sales").GetDecimal() +
            summary.GetProperty("transfersOut").GetDecimal() +
            summary.GetProperty("adjustments").GetDecimal());
        Assert.Equal(first.GetProperty("runningBalance").GetDecimal() -
            first.GetProperty("quantityDelta").GetDecimal(),
            second.GetProperty("runningBalance").GetDecimal());
        Assert.Equal(group.Count, (await Page(1)).GetProperty("totalItems").GetInt32());
        var kindCode = first.GetProperty("kind").GetString()!;
        var created = first.GetProperty("createdAtUtc").GetDateTimeOffset();
        var filtered = await Page(1, kindCode, created.AddSeconds(-1),
            created.AddSeconds(1), product.Code, category.Name);
        Assert.Equal(first.GetProperty("id").GetString(),
            filtered.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(onHand, filtered.GetProperty("items")[0]
            .GetProperty("runningBalance").GetDecimal());
        Assert.Equal(onHand, filtered.GetProperty("summary")
            .GetProperty("closingBalance").GetDecimal());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private sealed class VisualReadOnlyFactAttribute : FactAttribute
    {
        public VisualReadOnlyFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
                Skip = "Set CARNICERIAS_VISUAL_CONNECTION_STRING for the existing visual database.";
        }
    }
}
