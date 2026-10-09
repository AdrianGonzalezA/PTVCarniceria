using System.Net;
using System.Text;
using Carnicerias.Api.Fiscal;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaWsfeClientTests
{
    private static readonly ArcaAccessTicket Ticket = new("token<&", "firma", "20111111112");

    [Fact]
    public async Task ReadsLastAuthorizedNumberWithoutLeakingCredentialsIntoTheUrl()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body><FECompUltimoAutorizadoResponse xmlns="http://ar.gov.afip.dif.FEV1/">
                <FECompUltimoAutorizadoResult><PtoVta>12</PtoVta><CbteTipo>6</CbteTipo><CbteNro>42</CbteNro></FECompUltimoAutorizadoResult>
              </FECompUltimoAutorizadoResponse></soap:Body>
            </soap:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));

        var number = await client.GetLastAuthorizedAsync(Ticket, 12, 6);

        Assert.Equal(42, number);
        Assert.Equal("https://wswhomo.afip.gov.ar/wsfev1/service.asmx", handler.Url);
        Assert.Contains("FECompUltimoAutorizado", handler.Body);
        Assert.Contains("token&lt;&amp;", handler.Body);
        Assert.DoesNotContain("token", handler.Url);
        Assert.DoesNotContain(Ticket.Token, Ticket.ToString());
    }

    [Fact]
    public async Task LooksUpTheExactInvoiceAndItsAuthorizationForTimeoutRecovery()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body><FECompConsultarResponse xmlns="http://ar.gov.afip.dif.FEV1/">
                <FECompConsultarResult><ResultGet>
                  <CbteDesde>43</CbteDesde><CbteHasta>43</CbteHasta><PtoVta>12</PtoVta><CbteTipo>6</CbteTipo>
                  <ImpTotal>2375.00</ImpTotal><Resultado>A</Resultado>
                  <CodAutorizacion>12345678901234</CodAutorizacion><EmisionTipo>CAE</EmisionTipo><FchVto>20261019</FchVto>
                </ResultGet></FECompConsultarResult>
              </FECompConsultarResponse></soap:Body>
            </soap:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));

        var result = await client.ConsultAsync(Ticket, 12, 6, 43);

        Assert.Equal(2375m, result.Total);
        Assert.Equal(43, result.Number);
        Assert.Equal("12345678901234", result.AuthorizationCode);
        Assert.Equal("CAE", result.AuthorizationKind);
        Assert.Contains("<CbteNro>43</CbteNro>", handler.Body);
    }

    [Fact]
    public async Task AcceptsTheSoap12ResponseShapeShownInTheArcaManual()
    {
        var handler = new FakeHandler("""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body><FECompUltimoAutorizadoResponse>
                <FECompUltimoAutorizadoResult><PtoVta>12</PtoVta><CbteTipo>6</CbteTipo><CbteNro>0</CbteNro></FECompUltimoAutorizadoResult>
              </FECompUltimoAutorizadoResponse></soap12:Body>
            </soap12:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));

        Assert.Equal(0, await client.GetLastAuthorizedAsync(Ticket, 12, 6));
    }

    [Fact]
    public async Task RefusesAResponseForAnotherInvoiceOrUnsafeXml()
    {
        var mismatched = new ArcaWsfeClient(new HttpClient(new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
              <FECompConsultarResult><ResultGet><PtoVta>99</PtoVta><CbteTipo>6</CbteTipo>
                <CbteDesde>43</CbteDesde><CbteHasta>43</CbteHasta><ImpTotal>1</ImpTotal>
                <Resultado>A</Resultado><CodAutorizacion>123</CodAutorizacion><EmisionTipo>CAE</EmisionTipo>
              </ResultGet></FECompConsultarResult>
            </soap:Body></soap:Envelope>
            """)));
        await Assert.ThrowsAsync<InvalidDataException>(() => mismatched.ConsultAsync(Ticket, 12, 6, 43));

        var unsafeXml = new ArcaWsfeClient(new HttpClient(new FakeHandler("""
            <!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><foo>&xxe;</foo>
            """)));
        await Assert.ThrowsAnyAsync<Exception>(() => unsafeXml.GetLastAuthorizedAsync(Ticket, 12, 6));
    }

    private sealed class FakeHandler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "text/xml")
            };
        }
    }
}
