using System.Text.Json;
using Carnicerias.Api.Fiscal;
using Carnicerias.Api.Security;
using Carnicerias.Domain.PlatformAccess;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class FiscalSalesBookReadOnlyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [VisualReadOnlyFact]
    [Trait("Category", "VisualDatabaseReadOnly")]
    public async Task ListAndExportTranslateAgainstAuthorizedDatabaseWithoutWriting()
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
        var document = await db.FiscalDocuments.AsNoTracking()
            .Where(item => item.Status == FiscalDocumentStatus.Authorized)
            .OrderByDescending(item => item.IssueDate).FirstAsync();
        var sale = await db.ConfirmedSales.AsNoTracking()
            .SingleAsync(item => item.Id == document.SaleId);
        var accessor = new OperationalContextAccessor();
        accessor.Set(new OperationalContext(Guid.NewGuid(), document.CompanyId, sale.BranchId,
            new HashSet<string> { PlatformPermissionCatalog.OrganizationManage,
                PlatformPermissionCatalog.UsersManage }, Guid.NewGuid()), null);
        var from = document.IssueDate.AddDays(-30);
        var to = document.IssueDate;
        var expected = await db.FiscalDocuments.AsNoTracking().CountAsync(item =>
            item.CompanyId == document.CompanyId &&
            item.Status == FiscalDocumentStatus.Authorized &&
            item.IssueDate >= from && item.IssueDate <= to);
        var expectedComplete = await db.FiscalDocuments.AsNoTracking().CountAsync(item =>
            item.CompanyId == document.CompanyId &&
            item.Status == FiscalDocumentStatus.Authorized &&
            item.IssueDate >= from && item.IssueDate <= to &&
            item.Snapshot != null && item.Snapshot.Total == item.Total);

        var list = await FiscalSalesBookEndpoints.ListAsync(db, accessor, CancellationToken.None,
            from, to, null, 1, 30);
        var body = JsonSerializer.SerializeToElement(
            Assert.IsAssignableFrom<IValueHttpResult>(list).Value, JsonOptions);
        Assert.Equal(expected, body.GetProperty("totalItems").GetInt32());
        Assert.Equal(expectedComplete, body.GetProperty("totals").GetProperty("completeCount").GetInt32());
        Assert.Equal(expected, body.GetProperty("totals").GetProperty("completeCount").GetInt32() +
            body.GetProperty("totals").GetProperty("incompleteCount").GetInt32());
        Assert.All(body.GetProperty("items").EnumerateArray(), item =>
            Assert.True(item.GetProperty("snapshotStatus").GetString() is
                "complete" or "legacyIncomplete" or "inconsistent"));

        var branchList = await FiscalSalesBookEndpoints.ListAsync(db, accessor,
            CancellationToken.None, from, to, sale.BranchId, 1, 30);
        var branchBody = JsonSerializer.SerializeToElement(
            Assert.IsAssignableFrom<IValueHttpResult>(branchList).Value, JsonOptions);
        Assert.True(branchBody.GetProperty("totalItems").GetInt32() <= expected);
        var branchName = await db.Branches.AsNoTracking().Where(item => item.Id == sale.BranchId)
            .Select(item => item.Name).SingleAsync();
        Assert.All(branchBody.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal(branchName, item.GetProperty("branchName").GetString()));

        var foreignBranchId = await db.Branches.AsNoTracking()
            .Where(item => item.CompanyId != document.CompanyId)
            .Select(item => item.Id).FirstOrDefaultAsync();
        if (foreignBranchId != Guid.Empty)
        {
            var foreignBranch = await FiscalSalesBookEndpoints.ListAsync(db, accessor,
                CancellationToken.None, from, to, foreignBranchId);
            Assert.Equal(StatusCodes.Status404NotFound,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(foreignBranch).StatusCode);
        }

        var cashierAccessor = new OperationalContextAccessor();
        cashierAccessor.Set(new OperationalContext(Guid.NewGuid(), document.CompanyId,
            sale.BranchId, new HashSet<string> { PlatformPermissionCatalog.OrganizationManage },
            Guid.NewGuid()), null);
        var cashierResult = await FiscalSalesBookEndpoints.ListAsync(db, cashierAccessor,
            CancellationToken.None, from, to);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(cashierResult).StatusCode);
        var invalidPeriod = await FiscalSalesBookEndpoints.ListAsync(db, accessor,
            CancellationToken.None, to, from);
        Assert.Equal(StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidPeriod).StatusCode);

        var export = await FiscalSalesBookEndpoints.ExportAsync(db, accessor,
            CancellationToken.None, from, to, sale.BranchId);
        Assert.IsAssignableFrom<IFileHttpResult>(export);
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
