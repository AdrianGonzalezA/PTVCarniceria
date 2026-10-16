using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Bootstrap;

public static class CentralMigrationCommand
{
    public static async Task<IReadOnlyList<string>> RunAsync(
        string? connectionString,
        string? backupPath,
        string? backupSha256,
        CancellationToken cancellationToken)
    {
        CentralConnectionGuard.Validate(connectionString);
        CentralBackupGuard.Validate(backupPath, backupSha256, DateTimeOffset.UtcNow);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyDatabaseIdentityAsync(connection, cancellationToken);

        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql(connection, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "central_access"))
            .Options;
        await using var context = new CentralAccessDbContext(options);

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length > 0)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException("Central migrations remain pending after application.");
        }

        await VerifyDatabaseIdentityAsync(connection, cancellationToken);
        return pending;
    }

    internal static async Task VerifyDatabaseIdentityAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_database() = 'carnicerias_test_visual'
                AND (SELECT count(*) FROM pg_namespace
                     WHERE nspname IN ('platform_access', 'pos_sales')) = 2
            """;
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new InvalidOperationException(
                "Migration refused: the connection does not reach the existing visual POS database.");
        }
    }
}
