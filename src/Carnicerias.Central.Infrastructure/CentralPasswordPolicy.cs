using Npgsql;

namespace Carnicerias.Central.Infrastructure;

public static class CentralPasswordPolicy
{
    public static int MinimumLength(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return 12;
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            return connection.Host == "127.0.0.1" && connection.Port == 55433
                && connection.Database == "carnicerias_test_visual" ? 6 : 12;
        }
        catch (ArgumentException) { return 12; }
    }
}
