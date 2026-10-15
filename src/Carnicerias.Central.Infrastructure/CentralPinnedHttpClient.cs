using System.Net;
using System.Net.Sockets;

namespace Carnicerias.Central.Infrastructure;

internal static class CentralPinnedHttpClient
{
    public static HttpClient Create(CentralApprovedDestination approved, int maximumResponseBytes)
    {
        // Resolve and approve before connecting, then pin the checked IP while TLS validates the URI host.
        // https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!context.DnsEndPoint.Host.Equals(approved.Destination.Host,
                        StringComparison.OrdinalIgnoreCase)
                    || context.DnsEndPoint.Port != approved.Destination.Port)
                {
                    throw new InvalidOperationException("Unexpected branch connection target.");
                }

                var socket = new Socket(approved.PinnedAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(
                        approved.PinnedAddress, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
            MaxResponseContentBufferSize = maximumResponseBytes
        };
    }
}
