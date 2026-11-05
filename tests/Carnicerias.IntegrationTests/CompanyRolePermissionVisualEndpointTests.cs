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
public sealed class CompanyRolePermissionVisualEndpointTests
{
    [I18AReceiptApprovedVisualWriteFact]
    [Trait("Category", "I18AApprovedDatabaseWrite")]
    public async Task ReceiptPermissionIsPerCompanyAndChangesApplyToOpenSessions()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyA = new Company($"I18a role fixture {suffix} A", $"I18ARP-{suffix}A");
        var companyB = new Company($"I18a role fixture {suffix} B", $"I18ARP-{suffix}B");
        var branchA = new Branch(companyA.Id, "Sucursal A");
        var branchB = new Branch(companyB.Id, "Sucursal B");
        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(companyA.Id, branchA.Id, "Caja A");
        terminal.AssignCredentialHash(credential.Hash);
        var hasher = new Argon2idPasswordHasher();
        var password = $"I18ARP-{Guid.NewGuid():N}!";
        var adminA = UserIdentity.CreateForCompany(companyA.Id, $"i18ar-admin-{suffix}",
            $"i18ar-admin-{suffix}@example.test", hasher.Hash(password));
        var cashierA = UserIdentity.CreateForCompany(companyA.Id, $"i18ar-a-{suffix}",
            $"i18ar-a-{suffix}@example.test", hasher.Hash(password));
        var cashierB = UserIdentity.CreateForCompany(companyB.Id, $"i18ar-b-{suffix}",
            $"i18ar-b-{suffix}@example.test", hasher.Hash(password));
        Guid adminRoleId;
        Guid cashierRoleId;
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                adminRoleId = await db.Roles.Where(item => item.Code == "administrator")
                    .Select(item => item.Id).SingleAsync();
                cashierRoleId = await db.Roles.Where(item => item.Code == "cashier")
                    .Select(item => item.Id).SingleAsync();
                var permissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.InventoryReceiptsManage)
                    .Select(item => item.Id).SingleAsync();
                Assert.False(await db.RolePermissions.AnyAsync(item =>
                    item.RoleId == cashierRoleId && item.PermissionId == permissionId));
                db.AddRange(companyA, companyB, branchA, branchB, terminal,
                    adminA, cashierA, cashierB,
                    new UserAssignment(adminA.Id, adminRoleId, companyA.Id),
                    new UserAssignment(cashierA.Id, cashierRoleId, companyA.Id, branchA.Id),
                    new UserAssignment(cashierB.Id, cashierRoleId, companyB.Id, branchB.Id));
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            adminClient.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", credential.Token);
            await LoginAsync(adminClient, companyA, branchA, adminA, password);
            var initial = await OkAsync(await adminClient.GetAsync("/api/users/role-permissions"));
            Assert.Equal(2, initial.GetArrayLength());
            Assert.All(initial.EnumerateArray(), item =>
                Assert.False(item.GetProperty("isGranted").GetBoolean()));
            var url = "/api/users/role-permissions/cashier/inventory.receipts.manage";
            var granted = await OkAsync(await adminClient.PutAsJsonAsync(url,
                new { isGranted = true }));
            Assert.True(granted.GetProperty("isGranted").GetBoolean());

            using var cashierClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            cashierClient.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", credential.Token);
            await LoginAsync(cashierClient, companyA, branchA, cashierA, password);
            using (var allowed = await cashierClient.GetAsync(
                "/api/inventory/receipts/supplier-options"))
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using (var deniedManagement = await cashierClient.GetAsync(
                "/api/users/role-permissions"))
                Assert.Equal(HttpStatusCode.Forbidden, deniedManagement.StatusCode);

            await using (var db = new PlatformAccessDbContext(options))
            {
                var otherCompany = await new OperationalContextAccessService(db)
                    .ResolveAsync(cashierB.Id, Guid.NewGuid(), companyB.Id, branchB.Id);
                Assert.NotNull(otherCompany);
                Assert.DoesNotContain(PlatformPermissionCatalog.InventoryReceiptsManage,
                    otherCompany.Context.Permissions);
                Assert.Single(await db.CompanyRolePermissionOverrides.Where(item =>
                    item.CompanyId == companyA.Id).ToArrayAsync());
                Assert.False(await db.CompanyRolePermissionOverrides.AnyAsync(item =>
                    item.CompanyId == companyB.Id));
            }
            var revoked = await OkAsync(await adminClient.PutAsJsonAsync(url,
                new { isGranted = false }));
            Assert.False(revoked.GetProperty("isGranted").GetBoolean());
            using (var denied = await cashierClient.GetAsync(
                "/api/inventory/receipts/supplier-options"))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var repeated = await OkAsync(await adminClient.PutAsJsonAsync(url,
                new { isGranted = false }));
            Assert.False(repeated.GetProperty("isGranted").GetBoolean());
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                var ids = new[] { companyA.Id, companyB.Id };
                await db.Sessions.Where(item => item.CompanyId.HasValue &&
                    ids.Contains(item.CompanyId.Value)).ExecuteDeleteAsync();
                await db.CompanyRolePermissionOverrides.Where(item =>
                    ids.Contains(item.CompanyId)).ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => ids.Contains(item.CompanyId))
                    .ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => ids.Contains(item.CompanyId))
                    .ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId.HasValue &&
                    ids.Contains(item.CompanyId.Value)).ExecuteDeleteAsync();
                await db.Branches.Where(item => ids.Contains(item.CompanyId))
                    .ExecuteDeleteAsync();
                await db.Companies.Where(item => ids.Contains(item.Id))
                    .ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }
    }

    private static async Task LoginAsync(HttpClient client, Company company, Branch branch,
        UserIdentity user, string password)
    {
        using (var login = await client.PostAsJsonAsync("/api/sessions",
            new { companyCode = company.Code, credential = user.Username, password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            client.DefaultRequestHeaders.Add("Cookie",
                Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
        }
        using var context = await client.PutAsJsonAsync("/api/sessions/current/context",
            new { companyId = company.Id, branchId = branch.Id });
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Expected 200, received {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
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
                services.AddDbContext<PlatformAccessDbContext>(builder =>
                    builder.UseNpgsql(connectionString));
            });
        }
    }
}
