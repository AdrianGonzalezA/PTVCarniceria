using System.Net;
using System.Net.Http.Json;
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
public sealed class CashWithdrawalVisualEndpointTests
{
    [I16ApprovedVisualWriteFact]
    [Trait("Category", "I16ApprovedDatabaseWrite")]
    public async Task RequestDecisionAndShiftCloseAreIdempotentAndCashOnly()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("carnicerias_test_visual", target.Database);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I16 fixture {suffix}", $"I16-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I16");
        var cashierRole = new Role($"I16-cashier-{suffix}", "Cajero I16");
        var adminRole = new Role($"I16-admin-{suffix}", "Administrador I16");
        var password = $"I16-{Guid.NewGuid():N}!";
        var hasher = new Argon2idPasswordHasher();
        var cashier = UserIdentity.CreateForCompany(company.Id, $"i16cash-{suffix}",
            $"i16cash-{suffix}@example.test", hasher.Hash(password));
        var adminA = UserIdentity.CreateForCompany(company.Id, $"i16adma-{suffix}",
            $"i16adma-{suffix}@example.test", hasher.Hash(password));
        var adminB = UserIdentity.CreateForCompany(company.Id, $"i16admb-{suffix}",
            $"i16admb-{suffix}@example.test", hasher.Hash(password));
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Caja I16");
        terminal.AssignCredentialHash(credential.Hash);
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permission = await db.Permissions.SingleAsync(item =>
                    item.Code == PlatformPermissionCatalog.CashWithdrawalApprove);
                db.AddRange(company, branch, cashierRole, adminRole, cashier, adminA, adminB,
                    terminal, new RoleCapability(adminRole.Id, permission.Id),
                    new UserAssignment(cashier.Id, cashierRole.Id, company.Id),
                    new UserAssignment(adminA.Id, adminRole.Id, company.Id),
                    new UserAssignment(adminB.Id, adminRole.Id, company.Id));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var cashierClient = await LoginAsync(factory, company, branch,
                cashier, password, credential.Token);
            using var adminClientA = await LoginAsync(factory, company, branch,
                adminA, password);
            using var adminClientB = await LoginAsync(factory, company, branch,
                adminB, password);

            using var opened = await cashierClient.PostAsJsonAsync("/api/cashier-shifts",
                new { openingCash = 1000m });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var rejectedOperation = Guid.NewGuid();
            using var requestedReject = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/withdrawals",
                new { operationId = rejectedOperation, method = "cash", amount = 100m,
                    reason = "Solicitud rechazada" });
            Assert.Equal(HttpStatusCode.Created, requestedReject.StatusCode);
            var rejectedId = (await ReadAsync(requestedReject)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Forbidden,
                (await cashierClient.PostAsJsonAsync($"/api/admin/cash-withdrawals/{rejectedId}/decision",
                    new { action = "approve" })).StatusCode);
            using var rejected = await adminClientA.PostAsJsonAsync(
                $"/api/admin/cash-withdrawals/{rejectedId}/decision",
                new { action = "reject", reason = "No corresponde" });
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.Equal("rejected", (await ReadAsync(rejected)).GetProperty("status").GetString());

            using var oversized = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/withdrawals",
                new { operationId = Guid.NewGuid(), method = "cash", amount = 1200m,
                    reason = "Importe mayor al efectivo" });
            Assert.Equal(HttpStatusCode.Created, oversized.StatusCode);
            var oversizedId = (await ReadAsync(oversized)).GetProperty("id").GetGuid();
            using var deniedForBalance = await adminClientA.PostAsJsonAsync(
                $"/api/admin/cash-withdrawals/{oversizedId}/decision",
                new { action = "approve" });
            Assert.Equal(HttpStatusCode.Conflict, deniedForBalance.StatusCode);
            Assert.Equal("WITHDRAWAL_INSUFFICIENT_CASH",
                (await ReadAsync(deniedForBalance)).GetProperty("error")
                    .GetProperty("code").GetString());
            using var rejectedOversized = await adminClientA.PostAsJsonAsync(
                $"/api/admin/cash-withdrawals/{oversizedId}/decision",
                new { action = "reject", reason = "Saldo insuficiente" });
            Assert.Equal(HttpStatusCode.OK, rejectedOversized.StatusCode);

            var operationId = Guid.NewGuid();
            var payload = new { operationId, method = "cash", amount = 400m,
                reason = "Rendición controlada" };
            using var requested = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/withdrawals", payload);
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            var requestId = (await ReadAsync(requested)).GetProperty("id").GetGuid();
            using var retried = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/withdrawals", payload);
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            Assert.Equal(requestId, (await ReadAsync(retried)).GetProperty("id").GetGuid());
            using var prematureClose = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/close", new { });
            Assert.Equal(HttpStatusCode.Conflict, prematureClose.StatusCode);
            Assert.Equal("CASHIER_SHIFT_HAS_PENDING_WITHDRAWAL",
                (await ReadAsync(prematureClose)).GetProperty("error").GetProperty("code").GetString());

            using var pendingList = await adminClientA.GetAsync(
                "/api/admin/cash-withdrawals?status=pending");
            Assert.Equal(HttpStatusCode.OK, pendingList.StatusCode);
            Assert.Equal(requestId, (await ReadAsync(pendingList)).GetProperty("items")[0]
                .GetProperty("id").GetGuid());

            var decisions = await Task.WhenAll(
                adminClientA.PostAsJsonAsync($"/api/admin/cash-withdrawals/{requestId}/decision",
                    new { action = "approve" }),
                adminClientB.PostAsJsonAsync($"/api/admin/cash-withdrawals/{requestId}/decision",
                    new { action = "approve" }));
            Assert.All(decisions, response => Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            Assert.Contains(decisions, response => response.StatusCode == HttpStatusCode.OK);
            foreach (var response in decisions) response.Dispose();

            await using (var db = new PlatformAccessDbContext(options))
            {
                var movements = await db.CashLedgerMovements.AsNoTracking()
                    .Where(item => item.CompanyId == company.Id && item.OperationId == operationId)
                    .ToArrayAsync();
                var movement = Assert.Single(movements);
                Assert.Equal(-400m, movement.AmountDelta);
                Assert.Equal(CashLedgerMovementKind.ExtraordinaryWithdrawal, movement.Kind);
                var savedRequest = await db.CashWithdrawalRequests.AsNoTracking()
                    .SingleAsync(item => item.Id == requestId);
                Assert.Equal(CashWithdrawalStatus.Approved, savedRequest.Status);
                Assert.Equal(movement.Id, savedRequest.LedgerMovementId);
            }
            using var current = await cashierClient.GetAsync("/api/cashier-shifts/current");
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            var currentBody = await ReadAsync(current);
            Assert.Equal(600m, currentBody.GetProperty("cashBalance").GetDecimal());
            Assert.Equal(400m, currentBody.GetProperty("cashWithdrawals").GetDecimal());
            using var closed = await cashierClient.PostAsJsonAsync(
                "/api/cashier-shifts/current/close", new { });
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.CashWithdrawalRequests.Where(item => item.CompanyId == company.Id)
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
                await db.RolePermissions.Where(item => item.RoleId == adminRole.Id)
                    .ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == cashierRole.Id || item.Id == adminRole.Id)
                    .ExecuteDeleteAsync();
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

    private static async Task<HttpClient> LoginAsync(VisualApiFactory factory,
        Company company, Branch branch, UserIdentity user, string password,
        string? terminalCredential = null)
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

public sealed class I16ApprovedVisualWriteFactAttribute : FactAttribute
{
    public I16ApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I16_WRITES_APPROVED") != "I16_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
