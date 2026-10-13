namespace Carnicerias.Api.Payments;

public static class CloverTunnelBoundary
{
    public static bool Allows(string method, string? path) =>
        string.Equals(method, "GET", StringComparison.Ordinal) && path is not null &&
        (string.Equals(path, "/clover", StringComparison.Ordinal) ||
         string.Equals(path, "/clover/launch", StringComparison.Ordinal) ||
         string.Equals(path, "/clover/callback", StringComparison.Ordinal) ||
         string.Equals(path, "/clover/status", StringComparison.Ordinal));
}
