using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralInstallationProbeTests
{
    private static readonly string[] SummaryCapability = ["summary"];

    [Fact]
    public async Task PolicyRejectsUnapprovedUrlMixedDnsAndExternalHttp()
    {
        var approvedAddress = IPAddress.Parse("10.10.1.25");
        var resolver = new StubResolver([approvedAddress, IPAddress.Parse("169.254.169.254")]);
        var policy = new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri("https://branch.example:8443/"), [approvedAddress])],
            allowLoopbackHttp: false, resolver);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveAsync("https://unlisted.example/", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveAsync("http://branch.example:8443/", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveAsync("https://branch.example:8443/", CancellationToken.None));

        resolver.Addresses = [approvedAddress];
        var approved = await policy.ApproveAsync("https://branch.example:8443/", CancellationToken.None);
        Assert.Equal(approvedAddress, approved.PinnedAddress);
    }

    [Fact]
    public async Task LoopbackHttpRequiresExplicitApproval()
    {
        var endpoint = new CentralApprovedEndpoint(
            new Uri("http://127.0.0.1:5246/"), [IPAddress.Loopback]);
        var denied = new CentralDestinationPolicy([endpoint], false, new StubResolver([IPAddress.Loopback]));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            denied.ApproveAsync(endpoint.Destination.AbsoluteUri, CancellationToken.None));

        var allowed = new CentralDestinationPolicy([endpoint], true, new StubResolver([IPAddress.Loopback]));
        Assert.Equal(IPAddress.Loopback,
            (await allowed.ApproveAsync(endpoint.Destination.AbsoluteUri, CancellationToken.None)).PinnedAddress);
    }

    [Fact]
    public async Task WebRegistrationRequiresAnEndpointApprovedForItsOwnCentralCompany()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var url = "https://branch.example:8443/";
        var address = IPAddress.Parse("10.10.1.25");
        var policy = new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri(url), [address], companyA),
                new CentralApprovedEndpoint(new Uri("https://legacy.example/"), [address])],
            false, new StubResolver([address]));

        Assert.Equal(address,
            (await policy.ApproveForCompanyAsync(url, companyA, CancellationToken.None)).PinnedAddress);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveForCompanyAsync(url, companyB, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveForCompanyAsync("https://legacy.example/", companyA, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ApproveAsync(url, CancellationToken.None));
    }

    [Fact]
    public async Task ProbeRequiresSecretExactIdentityAndDoesNotFollowRedirects()
    {
        var installationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var approved = await new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri("https://branch.example/"), [IPAddress.Parse("10.0.0.5")])],
            false, new StubResolver([IPAddress.Parse("10.0.0.5")]))
            .ApproveAsync("https://branch.example/", CancellationToken.None);
        var handler = new StubHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyWithClientAsync(client, approved, null,
                installationId, companyId, branchId, CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);

        handler.Response = new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://elsewhere.example/") }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyWithClientAsync(client, approved, new string('A', 64),
                installationId, companyId, branchId, CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);

        handler.Response = JsonResponse(installationId, Guid.NewGuid(), branchId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyWithClientAsync(client, approved, new string('A', 64),
                installationId, companyId, branchId, CancellationToken.None));

        handler.Response = JsonResponse(installationId, companyId, branchId);
        var result = await CentralInstallationProbe.VerifyWithClientAsync(client, approved,
            new string('A', 64), installationId, companyId, branchId, CancellationToken.None);
        Assert.Equal(installationId, result.InstallationId);
        Assert.Equal("Bearer " + new string('A', 64), handler.LastAuthorization);
        Assert.Equal("/api/central/v1/installation", handler.LastPath);
    }

    [Fact]
    public async Task ProductionProbeUsesPinnedHandlerAndRefusesActualHttpRedirect()
    {
        var installationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://127.0.0.1:{port}/";
        var approved = await new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri(url), [IPAddress.Loopback])],
            true, new StubResolver([IPAddress.Loopback]))
            .ApproveAsync(url, CancellationToken.None);
        using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = Task.Run(async () =>
        {
            using (var first = await listener.AcceptTcpClientAsync(serverTimeout.Token))
            {
                using var stream = first.GetStream();
                var requestBytes = new byte[2048];
                _ = await stream.ReadAsync(requestBytes, serverTimeout.Token);
                Assert.Contains("GET /api/central/v1/installation ",
                    Encoding.ASCII.GetString(requestBytes));
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 302 Found\r\nLocation: {url}redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, serverTimeout.Token);
            }

            using var redirectWait = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                using var second = await listener.AcceptTcpClientAsync(redirectWait.Token);
                using var stream = second.GetStream();
                var requestBytes = new byte[2048];
                _ = await stream.ReadAsync(requestBytes, serverTimeout.Token);
                var body = JsonSerializer.Serialize(new
                {
                    contractVersion = 1, installationId, companyId, branchId
                });
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, serverTimeout.Token);
                await stream.WriteAsync(bodyBytes, serverTimeout.Token);
                return 2;
            }
            catch (OperationCanceledException)
            {
                return 1;
            }
        }, serverTimeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyAsync(approved, new string('A', 64),
                installationId, companyId, branchId, CancellationToken.None));
        Assert.Equal(1, await server);
    }

    [Fact]
    public async Task ProductionProbeRejectsAnUntrustedTlsCertificate()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"https://127.0.0.1:{port}/";
        var approved = await new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri(url), [IPAddress.Loopback])],
            false, new StubResolver([IPAddress.Loopback]))
            .ApproveAsync(url, CancellationToken.None);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=untrusted-branch",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            using var tls = new System.Net.Security.SslStream(socket.GetStream(), false);
            try
            {
                await tls.AuthenticateAsServerAsync(certificate);
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException)
            {
                // The client can abort the handshake after rejecting the certificate.
            }
        }, timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyAsync(approved, new string('A', 64),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task ProductionProbeRejectsAnOfflineBranch()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = $"http://127.0.0.1:{port}/";
        var approved = await new CentralDestinationPolicy(
            [new CentralApprovedEndpoint(new Uri(url), [IPAddress.Loopback])],
            true, new StubResolver([IPAddress.Loopback]))
            .ApproveAsync(url, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralInstallationProbe.VerifyAsync(approved, new string('A', 64),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    private static HttpResponseMessage JsonResponse(Guid installationId, Guid companyId, Guid branchId) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                contractVersion = 1, installationId, companyId, branchId,
                companyName = "Visual", branchName = "Centro", capabilities = SummaryCapability
            }))
        };

    private sealed class StubResolver(IPAddress[] addresses) : ICentralDnsResolver
    {
        public IPAddress[] Addresses { get; set; } = addresses;
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(Addresses);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.NotFound);
        public int RequestCount { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(Response);
        }
    }
}
