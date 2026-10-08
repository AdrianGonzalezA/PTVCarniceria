using System.Text;
using Npgsql;

namespace Carnicerias.Infrastructure;

public static class VisualDevelopmentPasswordPolicy
{
    public static bool IsVisualDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            return connection.Host == "127.0.0.1" && connection.Port == 55433 &&
                connection.Database == "carnicerias_test_visual";
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static int MinimumLength(string? connectionString) => IsVisualDatabase(connectionString) ? 6 : 12;

    public static bool IsValid(string? password, string? connectionString) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= MinimumLength(connectionString) &&
        Encoding.UTF8.GetByteCount(password) <= 1024;
}
