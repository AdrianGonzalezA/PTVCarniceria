using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Carnicerias.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Carnicerias.IntegrationTests;

// This class is run explicitly, never as part of tests that provision or reset a database.
public sealed class BranchServiceVisualDatabaseTests
{
    [Fact]
    public async Task ReadsOnlyThePinnedActiveBranchFromTheExistingVisualDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set the existing visual database connection string to run this read-only test.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", connection.Database);
        Assert.Equal(55433, connection.Port);
        Assert.True(connection.Host is "127.0.0.1" or "localhost");

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var db = new PlatformAccessDbContext(options);
        var binding = await db.Branches.AsNoTracking().Where(branch => branch.IsActive)
            .Join(db.Companies.AsNoTracking().Where(company => company.IsActive),
                branch => branch.CompanyId, company => company.Id,
                (branch, company) => new { Branch = branch, Company = company })
            .OrderBy(row => row.Branch.Name).FirstAsync();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-31);
        var sales = db.ConfirmedSales.AsNoTracking().Where(sale =>
            sale.CompanyId == binding.Company.Id && sale.BranchId == binding.Branch.Id &&
            sale.ConfirmedAtUtc >= from && sale.ConfirmedAtUtc < to);
        var expectedCount = await sales.CountAsync();
        var expectedTotal = await sales.SumAsync(sale => (decimal?)sale.Total) ?? 0;
        var expectedPayments = await sales.SelectMany(sale => sale.Payments)
            .SumAsync(payment => (decimal?)payment.AppliedAmount) ?? 0;
        var openShifts = db.CashierShifts.AsNoTracking().Where(shift =>
            shift.CompanyId == binding.Company.Id && shift.BranchId == binding.Branch.Id &&
            shift.Status == CashierShiftStatus.Open);
        var expectedOpenShiftCount = await openShifts.CountAsync();
        var openShiftIds = openShifts.Select(shift => shift.Id);
        var expectedCash = await db.CashLedgerMovements.AsNoTracking().Where(movement =>
            movement.CompanyId == binding.Company.Id && movement.BranchId == binding.Branch.Id &&
            movement.Method == Carnicerias.Domain.Sales.PaymentMethod.Cash &&
            openShiftIds.Contains(movement.CashierShiftId))
            .SumAsync(movement => (decimal?)movement.AmountDelta) ?? 0;

        var installationId = Guid.NewGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PlatformAccess"] = connectionString,
            ["Federation:Enabled"] = "true",
            ["Federation:InstallationId"] = installationId.ToString(),
            ["Federation:CompanyId"] = binding.Company.Id.ToString(),
            ["Federation:BranchId"] = binding.Branch.Id.ToString(),
            ["Federation:CredentialSha256"] = Convert.ToHexString(
                SHA256.HashData(Convert.FromHexString(token)))
        };
        using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(config)));
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var identity = await client.GetAsync("/api/central/v1/installation");
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        using var identityJson = JsonDocument.Parse(await identity.Content.ReadAsStringAsync());
        Assert.Equal(installationId.ToString(), identityJson.RootElement.GetProperty("installationId").GetString());
        Assert.Equal(binding.Company.Name, identityJson.RootElement.GetProperty("companyName").GetString());
        Assert.Equal(binding.Branch.Name, identityJson.RootElement.GetProperty("branchName").GetString());

        var url = $"/api/central/v1/summary?fromUtc={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&toUtc={Uri.EscapeDataString(to.ToString("O"))}";
        using var summary = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal("no-store", summary.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(binding.Company.Id.ToString(), root.GetProperty("companyId").GetString());
        Assert.Equal(binding.Branch.Id.ToString(), root.GetProperty("branchId").GetString());
        Assert.Equal(expectedCount, root.GetProperty("saleCount").GetInt32());
        Assert.Equal(expectedTotal, root.GetProperty("salesTotal").GetDecimal());
        Assert.Equal(expectedPayments, root.GetProperty("salePaymentsTotal").GetDecimal());
        Assert.Equal(expectedOpenShiftCount, root.GetProperty("openShiftCount").GetInt32());
        Assert.Equal(expectedCash, root.GetProperty("openShiftCashBalance").GetDecimal());
        Assert.Equal("ARS", root.GetProperty("currency").GetString());
        Assert.False(root.TryGetProperty("recipientName", out _));
        Assert.False(root.TryGetProperty("customerId", out _));

        using var other = await client.GetAsync(url + "&branchId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        var invalidBinding = new Dictionary<string, string?>(config)
        {
            ["Federation:BranchId"] = Guid.NewGuid().ToString()
        };
        using var invalidApplication = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(invalidBinding)));
        using var invalidClient = invalidApplication.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await invalidClient.GetAsync("/api/central/v1/installation")).StatusCode);
    }
}
