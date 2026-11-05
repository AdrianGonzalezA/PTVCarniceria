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
public sealed class SupplierVisualEndpointTests
{
    [I18ASupplierApprovedVisualWriteFact]
    [Trait("Category", "I18AApprovedDatabaseWrite")]
    public async Task CrudIsCompanyScopedAndInactiveSuppliersStayInHistory()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = new Company($"I18a fixture {suffix}", $"I18A-{suffix}");
        var branch = new Branch(company.Id, "Sucursal I18a");
        var role = new Role($"I18A-{suffix}", "I18a admin");
        var password = $"I18A-{Guid.NewGuid():N}!";
        var user = UserIdentity.CreateForCompany(company.Id, $"i18a-{suffix}",
            $"i18a-{suffix}@example.test", new Argon2idPasswordHasher().Hash(password));
        var terminalCredential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(company.Id, branch.Id, "Caja I18a");
        terminal.AssignCredentialHash(terminalCredential.Hash);
        var seeded = false;
        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var permissionId = await db.Permissions.Where(item =>
                    item.Code == PlatformPermissionCatalog.OrganizationManage)
                    .Select(item => item.Id).SingleAsync();
                db.AddRange(company, branch, role, user,
                    new UserAssignment(user.Id, role.Id, company.Id),
                    new RoleCapability(role.Id, permissionId), terminal);
                await db.SaveChangesAsync();
                seeded = true;
            }

            using var factory = new VisualApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Pos-Terminal-Credential", terminalCredential.Token);
            using (var login = await client.PostAsJsonAsync("/api/sessions",
                new { companyCode = company.Code, credential = user.Username, password }))
            {
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                client.DefaultRequestHeaders.Add("Cookie",
                    Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
            }
            using (var context = await client.PutAsJsonAsync("/api/sessions/current/context",
                new { companyId = company.Id, branchId = branch.Id }))
                Assert.Equal(HttpStatusCode.OK, context.StatusCode);

            var created = await OkAsync(await client.PostAsJsonAsync("/api/admin/suppliers",
                new { code = " FR-1 ", name = "Frigorífico", identification = " 30710106513 " }),
                HttpStatusCode.Created);
            var supplierId = created.GetProperty("id").GetGuid();
            Assert.Equal("FR-1", created.GetProperty("code").GetString());
            Assert.Equal("30710106513", created.GetProperty("identification").GetString());

            using (var duplicate = await client.PostAsJsonAsync("/api/admin/suppliers",
                new { code = "fr-1", name = "Duplicado" }))
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var page = await OkAsync(await client.GetAsync("/api/admin/suppliers?page=1&search=FR-1"));
            Assert.Equal(1, page.GetProperty("totalItems").GetInt64());
            Assert.Equal(supplierId, page.GetProperty("items")[0].GetProperty("id").GetGuid());

            await OkAsync(await client.PatchAsJsonAsync($"/api/admin/suppliers/{supplierId}",
                new { code = "FR-1", name = "Nuevo nombre", identification = (string?)null }));
            await OkAsync(await client.PatchAsJsonAsync($"/api/admin/suppliers/{supplierId}",
                new { isActive = false }));
            var optionsResponse = await OkAsync(await client.GetAsync("/api/admin/suppliers/options"));
            Assert.Equal(0, optionsResponse.GetArrayLength());
            var retained = await OkAsync(await client.GetAsync("/api/admin/suppliers"));
            Assert.Equal("Nuevo nombre", retained.GetProperty("items")[0].GetProperty("name").GetString());
            Assert.False(retained.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
        }
        finally
        {
            if (seeded)
            {
                await using var db = new PlatformAccessDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Sessions.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Suppliers.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.UserAssignments.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.RolePermissions.Where(item => item.RoleId == role.Id).ExecuteDeleteAsync();
                await db.PosTerminals.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Users.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Branches.Where(item => item.CompanyId == company.Id).ExecuteDeleteAsync();
                await db.Roles.Where(item => item.Id == role.Id).ExecuteDeleteAsync();
                await db.Companies.Where(item => item.Id == company.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            }
        }
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected,
                $"Expected {(int)expected}, received {(int)response.StatusCode}: {body}");
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
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
            });
        }
    }
}

public sealed class I18ASupplierApprovedVisualWriteFactAttribute : FactAttribute
{
    public I18ASupplierApprovedVisualWriteFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CARNICERIAS_I18A_WRITES_APPROVED") != "I18A_APPROVED" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")))
            Skip = "Run only against the approved existing carnicerias_test_visual fixture.";
    }
}
