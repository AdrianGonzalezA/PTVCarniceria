using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class ShiftClosingReportVisualEndpointTests
{
    [I17ApprovedVisualWriteFact]
    [Trait("Category", "I17ApprovedDatabaseWrite")]
    public async Task CloseStoresSnapshotAndAdminDownloadsTheSameReport()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I17 fixture {suffix}", $"I17-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I17");
        var role = new Role($"I17-admin-{suffix}", "Administrador I17");
        var password = $"I17-{Guid.NewGuid():N}!";
        var hasher = new Argon2idPasswordHasher();
        var cashier = UserIdentity.CreateForCompany(company.Id, $"i17cash-{suffix}",
            $"i17cash-{suffix}@example.test", hasher.Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Caja I17");
        terminal.AssignCredentialHash(credential.Hash);
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permission = await db.Permissions.SingleAsync(item =>
                    item.Code == PlatformPermissionCatalog.OrganizationManage);
                db.AddRange(company, branch, role, cashier, terminal,
                    new RoleCapability(role.Id, permission.Id),
                    new UserAssignment(cashier.Id, role.Id, company.Id));
                await db.SaveChangesAsync();
                seeded = true;
            }
            using var factory = new VisualApiFactory(connectionString);
            using var terminalClient = await LoginAsync(factory, company, branch, cashier,
                password, credential.Token);
            using var adminClient = await LoginAsync(factory, company, branch, cashier, password);

            using var opened = await terminalClient.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 100m });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var shiftId = (await ReadAsync(opened)).GetProperty("id").GetGuid();
            using var invalid = await terminalClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/close", new { countedCash = 95m });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var preview = await terminalClient.GetAsync(
                "/api/cashier-shifts/current/close-preview");
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal(100m, (await ReadAsync(preview)).GetProperty("cashBalance").GetDecimal());

            using var closed = await terminalClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/close", new { countedCash = 95m, handedCash = 90m });
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            var report = (await ReadAsync(closed)).GetProperty("report");
            Assert.Equal(shiftId, report.GetProperty("shiftId").GetGuid());
            Assert.Equal(100m, report.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(95m, report.GetProperty("countedCash").GetDecimal());
            Assert.Equal(90m, report.GetProperty("handedCash").GetDecimal());
            Assert.Equal(-5m, report.GetProperty("cashDifference").GetDecimal());
            Assert.True(report.GetProperty("isComplete").GetBoolean());

            using var history = await adminClient.GetAsync("/api/admin/history/shifts");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var historyItems = (await ReadAsync(history)).GetProperty("items").EnumerateArray();
            Assert.Contains(historyItems, item => item.GetProperty("id").GetGuid() == shiftId &&
                item.GetProperty("hasReport").GetBoolean());
            using var saved = await adminClient.GetAsync($"/api/admin/history/shifts/{shiftId}/report");
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var savedReport = await ReadAsync(saved);
            Assert.Equal(report.GetProperty("cashDifference").GetDecimal(),
                savedReport.GetProperty("cashDifference").GetDecimal());
            using var pdf = await adminClient.GetAsync($"/api/admin/history/shifts/{shiftId}/report.pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            await using (var db = new PlatformAccessDbContext(options))
                Assert.Single(await db.ShiftClosingReports.Where(item => item.CompanyId == company.Id)
                    .ToArrayAsync());
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.ShiftClosingReports.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CashLedgerMovements.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.CashierShifts.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id)
                    .ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.RolePermissions.Where(item => item.RoleId == role.Id).ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<HttpClient> LoginAsync(VisualApiFactory factory, Company company,
        Branch branch, UserIdentity user, string password, string? terminalCredential = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        if (terminalCredential is not null)
            client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential);
        using var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie",
            Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        using var context = await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id });
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        return client;
    }

    private sealed class VisualApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:PlatformAccess"] = connectionString }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformAccessDbContext>();
                services.RemoveAll<DbContextOptions<PlatformAccessDbContext>>();
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
            });
        }
    }
}

public sealed class I17ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I17ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I17_WRITES_APPROVED") != "I17_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
