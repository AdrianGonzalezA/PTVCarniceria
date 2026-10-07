using System.Net;
using System.Net.Http.Json;
using Carnicerias.Api.Sessions;
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

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class SessionEndpointTests
{
    [PostgreSqlFact]
    public async Task LoginUsesGenericFailuresAndSecureRevocableCookie()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Database?.StartsWith("carnicerias_test_", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException("The integration database name must start with 'carnicerias_test_'.");
        }

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        const string password = "Correct Horse Battery 42!";
        var hasher = new Argon2idPasswordHasher();

        try
        {
            await using (var db = new PlatformAccessDbContext(options))
            {
                await db.Database.MigrateAsync();
                var bootstrap = new BootstrapAdminService(db, hasher);
                await bootstrap.CreateFirstAdministratorAsync(
                    "login-admin",
                    "login-admin@example.test",
                    "Test Company",
                    "Test Branch",
                    password,
                    []);

                var inactiveUser = UserIdentity.Create(
                    "inactive-user",
                    "inactive@example.test",
                    hasher.Hash(password));
                inactiveUser.Deactivate();
                db.Users.Add(inactiveUser);
                await db.SaveChangesAsync();
            }

            using var factory = new SessionApiFactory(connectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

            var unknown = await client.PostAsJsonAsync("/api/sessions", new { credential = "unknown", password });
            var incorrect = await client.PostAsJsonAsync("/api/sessions", new { credential = "login-admin", password = "Wrong password" });
            var inactive = await client.PostAsJsonAsync("/api/sessions", new { credential = "inactive-user", password });
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, incorrect.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
            Assert.Equal(await unknown.Content.ReadAsStringAsync(), await incorrect.Content.ReadAsStringAsync());
            Assert.Equal(await unknown.Content.ReadAsStringAsync(), await inactive.Content.ReadAsStringAsync());

            var login = await client.PostAsJsonAsync("/api/sessions", new { credential = "LOGIN-ADMIN", password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookieHeader = Assert.Single(login.Headers.GetValues("Set-Cookie"));
            Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/api", cookieHeader, StringComparison.OrdinalIgnoreCase);

            var credentialCookie = cookieHeader.Split(';', 2)[0];
            var credential = credentialCookie.Split('=', 2)[1];
            var loginBody = await login.Content.ReadAsStringAsync();
            Assert.DoesNotContain(credential, loginBody, StringComparison.Ordinal);

            using var currentRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sessions/current");
            currentRequest.Headers.Add("Cookie", credentialCookie);
            var current = await client.SendAsync(currentRequest);
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Contains("login-admin", await current.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var csrfRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/sessions/current");
            csrfRequest.Headers.Add("Cookie", credentialCookie);
            csrfRequest.Headers.Add("Origin", "https://attacker.example");
            var csrfResponse = await client.SendAsync(csrfRequest);
            Assert.Equal(HttpStatusCode.Forbidden, csrfResponse.StatusCode);

            using var logoutRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/sessions/current");
            logoutRequest.Headers.Add("Cookie", credentialCookie);
            logoutRequest.Headers.Add("Origin", "app://bundle");
            var logout = await client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Single(logout.Headers.GetValues("Set-Cookie"));

            using var revokedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sessions/current");
            revokedRequest.Headers.Add("Cookie", credentialCookie);
            var revoked = await client.SendAsync(revokedRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

            await using var verifyDb = new PlatformAccessDbContext(options);
            var session = await verifyDb.Sessions.SingleAsync();
            Assert.NotNull(session.RevokedAtUtc);
            Assert.NotEqual(credential, session.TokenHash);
        }
        finally
        {
            await using var cleanup = new PlatformAccessDbContext(options);
            await cleanup.Database.MigrateAsync("0");
        }
    }

    private sealed class SessionApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PlatformAccess"] = connectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformAccessDbContext>();
                services.RemoveAll<DbContextOptions<PlatformAccessDbContext>>();
                services.AddDbContext<PlatformAccessDbContext>(options => options.UseNpgsql(connectionString));
            });
        }
    }
}
