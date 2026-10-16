namespace Carnicerias.Central.Api;

internal static class CentralRequestSecurity
{
    public static bool HasSameOriginAndCsrfHeader(HttpContext context)
    {
        if (context.Request.Headers["X-Carnicerias-CSRF"] != "1"
            || context.Request.Headers.Origin.Count != 1
            || !Uri.TryCreate(context.Request.Headers.Origin[0], UriKind.Absolute, out var origin))
            return false;

        var request = context.Request;
        var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && origin.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == requestPort
            && origin.UserInfo.Length == 0
            && origin.AbsolutePath == "/"
            && origin.Query.Length == 0
            && origin.Fragment.Length == 0;
    }
}
