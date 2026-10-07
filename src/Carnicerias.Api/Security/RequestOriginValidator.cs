namespace Carnicerias.Api.Security;

public static class RequestOriginValidator
{
    private const string ElectronOrigin = "app://bundle";

    public static bool IsAllowed(string origin, HttpRequest request) =>
        string.Equals(origin, ElectronOrigin, StringComparison.Ordinal) ||
        (Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) &&
         string.Equals(parsedOrigin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(parsedOrigin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase));
}
