using Carnicerias.Api.Fiscal;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class FiscalIssuanceLockVisualTests
{
    [VisualReadOnlyFact]
    [Trait("Category", "VisualDatabaseReadOnly")]
    public async Task TwoConnectionsCannotIssueTheSameFiscalSequenceAtOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CONNECTION_STRING")!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(55433, target.Port);
        Assert.Equal("carnicerias_test_visual", target.Database);

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var first = new PlatformAccessDbContext(options);
        await using var second = new PlatformAccessDbContext(options);
        var acquired = await FiscalIssuanceLock.TryAcquireAsync(first, "30710106513", 99, 1,
            CancellationToken.None);
        Assert.NotNull(acquired);
        try
        {
            Assert.Null(await FiscalIssuanceLock.TryAcquireAsync(second, "30710106513", 99, 1,
                CancellationToken.None));
        }
        finally
        {
            await acquired.DisposeAsync();
        }

        var afterRelease = await FiscalIssuanceLock.TryAcquireAsync(second,
            "30710106513", 99, 1, CancellationToken.None);
        Assert.NotNull(afterRelease);
        await afterRelease.DisposeAsync();
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
