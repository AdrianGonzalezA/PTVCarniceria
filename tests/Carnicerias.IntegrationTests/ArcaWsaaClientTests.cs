using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Carnicerias.Api.Fiscal;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaWsaaClientTests
{
    [Fact]
    public async Task ReportsOnlyTheSafeFaultCodeOnHttp500()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FiscalTest", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var client = new ArcaWsaaClient(new HttpClient(new FaultHandler()), TimeProvider.System);

        var error = await Assert.ThrowsAsync<ArcaWsaaFaultException>(() =>
            client.RequestTicketAsync(certificate, "20111111112"));

        Assert.Equal("coe.alreadyAuthenticated", error.Code);
        Assert.DoesNotContain("secret-token", error.ToString());
    }

    [Fact]
    public async Task SignsAServiceTicketWithTheProvidedCertificateAndReadsTheResponse()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FiscalTest", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var handler = new FakeHandler();
        var client = new ArcaWsaaClient(new HttpClient(handler), TimeProvider.System);

        var ticket = await client.RequestTicketAsync(certificate, "20111111112");

        Assert.Equal("token-prueba", ticket.Token);
        Assert.Equal("firma-prueba", ticket.Sign);
        Assert.Equal("20111111112", ticket.Cuit);
        Assert.Equal("https://wsaahomo.afip.gov.ar/ws/services/LoginCms", handler.Url);
        var envelope = XDocument.Parse(handler.Body);
        var cmsText = envelope.Descendants().Single(element => element.Name.LocalName == "in0").Value;
        var cms = new SignedCms();
        cms.Decode(Convert.FromBase64String(cmsText));
        cms.CheckSignature(true);
        var loginRequest = XDocument.Parse(Encoding.UTF8.GetString(cms.ContentInfo.Content));
        Assert.Equal("wsfe", loginRequest.Descendants("service").Single().Value);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var expiry = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
            var ticketXml = new XElement("loginTicketResponse",
                new XElement("header", new XElement("expirationTime", expiry)),
                new XElement("credentials", new XElement("token", "token-prueba"),
                    new XElement("sign", "firma-prueba")));
            var envelope = new XElement("loginCmsResponse",
                new XElement("loginCmsReturn", ticketXml.ToString(SaveOptions.DisableFormatting)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope.ToString(), Encoding.UTF8, "text/xml")
            };
        }
    }

    private sealed class FaultHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""
                <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
                  <soap:Fault><faultcode>coe.alreadyAuthenticated</faultcode>
                    <faultstring>secret-token must never be logged</faultstring></soap:Fault>
                </soap:Body></soap:Envelope>
                """, Encoding.UTF8, "text/xml")
        });
    }
}
