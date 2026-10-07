namespace Carnicerias.Api.Security;

public static class OperationalEndpointExtensions
{
    public static RouteHandlerBuilder RequireOperationalContext(this RouteHandlerBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddEndpointFilter(new OperationalAuthorizationFilter(null));
    }

    public static RouteHandlerBuilder RequireOperationalPermission(
        this RouteHandlerBuilder endpoint,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return endpoint.AddEndpointFilter(new OperationalAuthorizationFilter(permission));
    }

    public static RouteGroupBuilder RequireOperationalPermission(
        this RouteGroupBuilder endpoints,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return endpoints.AddEndpointFilter(new OperationalAuthorizationFilter(permission));
    }
}
