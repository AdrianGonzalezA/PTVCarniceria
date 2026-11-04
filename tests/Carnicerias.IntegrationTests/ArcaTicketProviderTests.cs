using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Carnicerias.Api.Fiscal;

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
        using var provider = new ArcaHomologationTicketProvider(wsaa, TimeProvider.System);
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

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
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
