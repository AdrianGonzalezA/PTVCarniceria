using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Carnicerias.Api.Fiscal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaTicketProviderTests
{
    [Fact]
    public async Task KeepsInvoiceAndRegistryTicketsSeparate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TicketCacheTest", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var handler = new Handler();
        var wsaa = new ArcaWsaaClient(new HttpClient(handler), TimeProvider.System);
        var directory = Path.Combine(Path.GetTempPath(), "carnicerias-wsaa-tests", Guid.NewGuid().ToString("N"));
        var cache = CreateCache(directory);
        using var provider = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System, cache);
        var settings = new ArcaRuntimeSettings(Guid.NewGuid(), "20164755100", 99,
            "Emisor de prueba", "Calle de prueba", null, null,
            certificate.Export(X509ContentType.Pkcs12), null, null, certificate.Thumbprint);

        var invoice = await provider.GetAsync(settings, CancellationToken.None);
        var registry = await provider.GetAsync(settings, ArcaWsaaClient.RegistryService,
            CancellationToken.None);
        var cachedInvoice = await provider.GetAsync(settings, CancellationToken.None);

        Assert.Equal("wsfe-token", invoice.Token);
        Assert.Equal("ws_sr_constancia_inscripcion-token", registry.Token);
        Assert.Same(invoice, cachedInvoice);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ReusesEncryptedTicketAfterProviderRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "carnicerias-wsaa-tests", Guid.NewGuid().ToString("N"));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TicketCacheRestartTest", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var settings = new ArcaRuntimeSettings(Guid.NewGuid(), "20164755100", 99,
            "Emisor de prueba", "Calle de prueba", null, null,
            certificate.Export(X509ContentType.Pkcs12), null, null, certificate.Thumbprint);
        var handler = new Handler();
        var wsaa = new ArcaWsaaClient(new HttpClient(handler), TimeProvider.System);
        var cache = CreateCache(directory);

        using (var first = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System, cache))
            Assert.Equal("ws_sr_constancia_inscripcion-token", (await first.GetAsync(settings,
                ArcaWsaaClient.RegistryService, CancellationToken.None)).Token);
        using (var restarted = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System,
                   CreateCache(directory)))
            Assert.Equal("ws_sr_constancia_inscripcion-token", (await restarted.GetAsync(settings,
                ArcaWsaaClient.RegistryService, CancellationToken.None)).Token);

        Assert.Equal(1, handler.Calls);
        var saved = Directory.GetFiles(Path.Combine(directory, "tickets"), "*.ticket");
        Assert.Single(saved);
        Assert.DoesNotContain("ws_sr_constancia_inscripcion-token",
            Encoding.UTF8.GetString(await File.ReadAllBytesAsync(saved[0])));
    }

    [Fact]
    public async Task ConcurrentProvidersIssueOnlyOneTicketForSameCredential()
    {
        var directory = Path.Combine(Path.GetTempPath(), "carnicerias-wsaa-tests", Guid.NewGuid().ToString("N"));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TicketCacheConcurrencyTest", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var settings = new ArcaRuntimeSettings(Guid.NewGuid(), "20164755100", 99,
            "Emisor de prueba", "Calle de prueba", null, null,
            certificate.Export(X509ContentType.Pkcs12), null, null, certificate.Thumbprint);
        var handler = new Handler();
        var wsaa = new ArcaWsaaClient(new HttpClient(handler), TimeProvider.System);
        using var first = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System,
            CreateCache(directory));
        using var second = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System,
            CreateCache(directory));

        var tickets = await Task.WhenAll(
            first.GetAsync(settings, ArcaWsaaClient.RegistryService, CancellationToken.None),
            second.GetAsync(settings, ArcaWsaaClient.RegistryService, CancellationToken.None));

        Assert.All(tickets, ticket => Assert.Equal("ws_sr_constancia_inscripcion-token", ticket.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static ArcaWsaaTicketCache CreateCache(string directory)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")),
            options => options.SetApplicationName("Carnicerias.Fiscal.Tests"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ARCA_WSAA_CACHE_DIRECTORY"] = Path.Combine(directory, "tickets") })
            .Build();
        return new ArcaWsaaTicketCache(protection, configuration,
            NullLogger<ArcaWsaaTicketCache>.Instance);
    }

    private sealed class Handler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var cms = new SignedCms();
            cms.Decode(Convert.FromBase64String(envelope.Descendants()
                .Single(item => item.Name.LocalName == "in0").Value));
            var login = XDocument.Parse(Encoding.UTF8.GetString(cms.ContentInfo.Content));
            var service = login.Descendants("service").Single().Value;
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
            var ticket = new XElement("loginTicketResponse",
                new XElement("header", new XElement("expirationTime", expiry),
                    new XElement("service", service)),
                new XElement("credentials", new XElement("token", service + "-token"),
                    new XElement("sign", "test-sign")));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new XElement("loginCmsResponse",
                    new XElement("loginCmsReturn", ticket.ToString(SaveOptions.DisableFormatting)))
                    .ToString(), Encoding.UTF8, "text/xml")
            };
        }
    }
}
