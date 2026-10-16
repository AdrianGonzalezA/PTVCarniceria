using System.Net;
using System.Net.Sockets;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralApprovedEndpoint(Uri Destination, IReadOnlyList<IPAddress> AllowedAddresses,
    Guid? CentralCompanyId = null);
public sealed class CentralApprovedDestination
{
    internal CentralApprovedDestination(Uri destination, IPAddress pinnedAddress)
    {
        Destination = destination;
        PinnedAddress = pinnedAddress;
    }

    public Uri Destination { get; }
    public IPAddress PinnedAddress { get; }
}

public interface ICentralDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class CentralDnsResolver : ICentralDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        IPAddress.TryParse(host, out var address)
            ? Task.FromResult(new[] { address })
            : Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class CentralDestinationPolicy(
    IReadOnlyList<CentralApprovedEndpoint> approvedEndpoints,
    bool allowLoopbackHttp,
    ICentralDnsResolver dnsResolver)
{
    public Task<CentralApprovedDestination> ApproveAsync(
        string? destinationUrl, CancellationToken cancellationToken) =>
        ApproveForScopeAsync(destinationUrl, null, cancellationToken);

    public Task<CentralApprovedDestination> ApproveForCompanyAsync(
        string? destinationUrl, Guid centralCompanyId, CancellationToken cancellationToken) =>
        centralCompanyId == Guid.Empty
            ? throw Rejected()
            : ApproveForScopeAsync(destinationUrl, centralCompanyId, cancellationToken);

    private async Task<CentralApprovedDestination> ApproveForScopeAsync(
        string? destinationUrl, Guid? centralCompanyId, CancellationToken cancellationToken)
    {
        if (destinationUrl is null || destinationUrl.Length > 2048
            || !Uri.TryCreate(destinationUrl, UriKind.Absolute, out var destination)
            || destination.Scheme is not ("http" or "https")
            || destination.UserInfo.Length > 0 || destination.Query.Length > 0
            || destination.Fragment.Length > 0 || destination.AbsolutePath != "/")
        {
            throw Rejected();
        }

        var approved = approvedEndpoints.SingleOrDefault(entry =>
            string.Equals(entry.Destination.AbsoluteUri, destination.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase)
            && entry.CentralCompanyId == centralCompanyId);
        if (approved is null || approved.AllowedAddresses.Count == 0)
        {
            throw Rejected();
        }

        if (destination.Scheme == "http" && (!allowLoopbackHttp
            || destination.Host != "127.0.0.1"
            || approved.AllowedAddresses.Any(address => !address.Equals(IPAddress.Loopback))))
        {
            throw Rejected();
        }

        IPAddress[] resolved;
        try
        {
            resolved = await dnsResolver.ResolveAsync(destination.Host, cancellationToken);
        }
        catch (SocketException)
        {
            throw Rejected();
        }

        if (resolved.Length == 0 || resolved.Any(address => IsForbidden(address)
            || !approved.AllowedAddresses.Contains(address)))
        {
            throw Rejected();
        }

        return new CentralApprovedDestination(destination, resolved[0]);
    }

    private static bool IsForbidden(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || (address.IsIPv4MappedToIPv6 && IsForbidden(address.MapToIPv4()));
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or >= 224
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 127 && !address.Equals(IPAddress.Loopback);
    }

    private static InvalidOperationException Rejected() =>
        new("Branch destination is not approved for the central installation.");
}
