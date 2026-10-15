using System.Text;
using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Bootstrap;

internal static class CentralBootstrapCommand
{
    public static async Task<Guid> CreateCompanyAsync(
        string? connectionString,
        string companyCode,
        string companyName,
        string username,
        CancellationToken cancellationToken)
    {
        await using var context = await OpenVerifiedContextAsync(connectionString, cancellationToken);
        var password = ReadConfirmedPassword();
        return await new CentralBootstrapService(context, new CentralPasswordHasher(6))
            .CreateCompanyAsync(companyCode, companyName, username, password, cancellationToken);
    }

    public static async Task ResetPasswordAsync(
        string? connectionString,
        string companyCode,
        string username,
        CancellationToken cancellationToken)
    {
        await using var context = await OpenVerifiedContextAsync(connectionString, cancellationToken);
        var password = ReadConfirmedPassword();
        await new CentralBootstrapService(context, new CentralPasswordHasher(6))
            .ResetPasswordAsync(companyCode, username, password, cancellationToken);
    }

    internal static async Task<CentralAccessDbContext> OpenVerifiedContextAsync(
        string? connectionString,
        CancellationToken cancellationToken)
    {
        CentralConnectionGuard.Validate(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await CentralMigrationCommand.VerifyDatabaseIdentityAsync(connection, cancellationToken);

        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "central_access"))
            .Options;
        var context = new CentralAccessDbContext(options);
        try
        {
            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                throw new InvalidOperationException("Apply central migrations before central maintenance commands.");
            }

            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    internal static string ReadConfirmedPassword()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("An interactive terminal is required for central passwords.");
        }

        Console.Write("Central password (at least 6 characters in the visual lab): ");
        var first = ReadHiddenLine();
        Console.Write("Repeat central password: ");
        var second = ReadHiddenLine();
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Passwords did not match; no account was changed.");
        }

        return first;
    }

    private static string ReadHiddenLine()
    {
        var characters = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return characters.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Length > 0)
                {
                    characters.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                if (characters.Length == 1024)
                {
                    throw new InvalidOperationException("Central password input is too long.");
                }

                characters.Append(key.KeyChar);
            }
        }
    }
}
