using Npgsql;

namespace Carnicerias.Central.Bootstrap;

public static class CentralConnectionGuard
{
    public const string ExpectedHost = "127.0.0.1";
    public const int ExpectedPort = 55433;
    public const string ExpectedDatabase = "carnicerias_test_visual";

    public static void Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw InvalidTarget();
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw InvalidTarget();
        }

        if (parsed.Host != ExpectedHost
            || parsed.Port != ExpectedPort
            || parsed.Database != ExpectedDatabase
            || string.IsNullOrWhiteSpace(parsed.Username)
            || !string.IsNullOrWhiteSpace(parsed.SearchPath)
            || !string.IsNullOrWhiteSpace(parsed.Options)
            || parsed.LoadBalanceHosts)
        {
            throw InvalidTarget();
        }
    }

    private static InvalidOperationException InvalidTarget() =>
        new("Migration refused: only the existing visual database at 127.0.0.1:55433 is permitted.");
}
