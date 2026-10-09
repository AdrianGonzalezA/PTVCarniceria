using System.Net;
using System.Text;
using Carnicerias.Api.Fiscal;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaWsfeClientTests
{
    private static readonly ArcaAccessTicket Ticket = new("token<&", "firma", "20111111112");

    [Fact]
    public async Task ListsHomologationPointsOfSaleWithoutIssuingAnInvoice()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body><FEParamGetPtosVentaResponse xmlns="http://ar.gov.afip.dif.FEV1/">
                <FEParamGetPtosVentaResult><ResultGet>
                  <PtoVenta><Nro>12</Nro><EmisionTipo>CAE</EmisionTipo><Bloqueado>N</Bloqueado></PtoVenta>
                  <PtoVenta><Nro>13</Nro><EmisionTipo>CAE</EmisionTipo><Bloqueado>S</Bloqueado><FchBaja>20261001</FchBaja></PtoVenta>
                </ResultGet></FEParamGetPtosVentaResult>
              </FEParamGetPtosVentaResponse></soap:Body>
            </soap:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));

        var points = await client.GetPointsOfSaleAsync(Ticket);

        Assert.Equal(2, points.Count);
        Assert.Equal(12, points[0].Number);
        Assert.False(points[0].IsBlocked);
        Assert.Null(points[0].DeactivatedOn);
        Assert.True(points[1].IsBlocked);
        Assert.Equal(new DateOnly(2026, 10, 1), points[1].DeactivatedOn);
        Assert.Contains("FEParamGetPtosVenta", handler.Body);
        Assert.DoesNotContain("FECAESolicitar", handler.Body);
        Assert.DoesNotContain(Ticket.Token, handler.Url);
    }

    [Fact]
    public async Task RejectsMalformedPointOfSaleResults()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
              <FEParamGetPtosVentaResult><ResultGet><PtoVenta><Nro>12</Nro>
                <EmisionTipo>CAE</EmisionTipo><Bloqueado>?</Bloqueado>
              </PtoVenta></ResultGet></FEParamGetPtosVentaResult>
            </soap:Body></soap:Envelope>
            """);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArcaWsfeClient(new HttpClient(handler)).GetPointsOfSaleAsync(Ticket));
    }

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

    [Fact]
    public async Task BuildsAnExplicitBalancedCaeRequestAndAcceptsOnlyTheMatchingAuthorization()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body><FECAESolicitarResponse xmlns="http://ar.gov.afip.dif.FEV1/">
                <FECAESolicitarResult><FeCabResp><PtoVta>12</PtoVta><CbteTipo>6</CbteTipo><CantReg>1</CantReg></FeCabResp>
                  <FeDetResp><FECAEDetResponse><CbteDesde>43</CbteDesde><CbteHasta>43</CbteHasta>
                    <Resultado>A</Resultado><CAE>12345678901234</CAE><CAEFchVto>20261019</CAEFchVto>
                  </FECAEDetResponse></FeDetResp>
                </FECAESolicitarResult>
              </FECAESolicitarResponse></soap:Body>
            </soap:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));
        var request = new ArcaCaeRequest(12, 6, 43, new DateOnly(2026, 10, 9),
            80, 20111111112, 1, 121m, 100m, 0m, 0m,
            [new ArcaVatAmount(5, 100m, 21m)]);

        var result = await client.RequestCaeAsync(Ticket, request);

        Assert.Equal("12345678901234", result.Cae);
        Assert.Contains("<CondicionIVAReceptorId>1</CondicionIVAReceptorId>", handler.Body);
        Assert.Contains("<ImpNeto>100.00</ImpNeto>", handler.Body);
        Assert.Contains("<Importe>21.00</Importe>", handler.Body);
        Assert.Throws<ArgumentException>(() => new ArcaCaeRequest(12, 6, 43,
            new DateOnly(2026, 10, 9), 80, 20111111112, 1, 122m, 100m, 0m, 0m,
            [new ArcaVatAmount(5, 100m, 21m)]));
    }

    [Fact]
    public async Task NeverTreatsARejectedCaeResponseAsAnAuthorizedInvoice()
    {
        var handler = new FakeHandler("""
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
              <FECAESolicitarResult><FeCabResp><PtoVta>12</PtoVta><CbteTipo>6</CbteTipo><CantReg>1</CantReg></FeCabResp>
                <FeDetResp><FECAEDetResponse><CbteDesde>43</CbteDesde><CbteHasta>43</CbteHasta>
                  <Resultado>R</Resultado><CAE></CAE><CAEFchVto></CAEFchVto>
                </FECAEDetResponse></FeDetResp>
              </FECAESolicitarResult>
            </soap:Body></soap:Envelope>
            """);
        var client = new ArcaWsfeClient(new HttpClient(handler));
        var request = new ArcaCaeRequest(12, 6, 43, new DateOnly(2026, 10, 9),
            80, 20111111112, 1, 121m, 100m, 0m, 0m,
            [new ArcaVatAmount(5, 100m, 21m)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.RequestCaeAsync(Ticket, request));
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
