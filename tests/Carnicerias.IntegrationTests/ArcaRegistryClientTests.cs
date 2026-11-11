using System.Net;
using System.Text;
using System.Xml.Linq;
using Carnicerias.Api.Customers;
using Carnicerias.Api.Fiscal;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaRegistryClientTests
{
    private const string Cuit = "20164755100";
    private static readonly ArcaAccessTicket Ticket = new("token<&", "sign", Cuit);

    [Fact]
    public async Task SendsHomologationRequestAndMapsActiveRegisteredTaxpayer()
    {
        var handler = new Handler(Response("<datosRegimenGeneral><impuesto>" +
            "<idImpuesto>30</idImpuesto><estadoImpuesto>AC</estadoImpuesto>" +
            "</impuesto></datosRegimenGeneral>"));
        var person = await new ArcaRegistryClient(new HttpClient(handler))
            .GetPersonAsync(Ticket, Cuit);

        Assert.Equal("https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA5",
            handler.Url);
        var request = XDocument.Parse(handler.Body);
        Assert.Equal("getPersona_v2", request.Descendants()
            .Single(item => item.Name.LocalName == "getPersona_v2").Name.LocalName);
        Assert.Equal("token<&", request.Descendants()
            .Single(item => item.Name.LocalName == "token").Value);
        Assert.Equal(Cuit, request.Descendants()
            .Single(item => item.Name.LocalName == "idPersona").Value);
        Assert.NotNull(person);
        Assert.Equal("GARCIA ANA", person.Name);
        Assert.Equal("SAN MANUEL 9", person.Address);
        Assert.Equal("SAN MANUEL", person.Locality);
        Assert.Equal("BUENOS AIRES", person.Province);
        Assert.Equal("registered", person.TaxStatus);
    }

    [Fact]
    public async Task MapsSmallTaxpayerOnlyWhenMonotributoIsActiveAndUnambiguous()
    {
        var client = new ArcaRegistryClient(new HttpClient(new Handler(Response(
            "<datosMonotributo><impuesto><idImpuesto>20</idImpuesto>" +
            "<estadoImpuesto>AC</estadoImpuesto></impuesto></datosMonotributo>"))));
        var person = await client.GetPersonAsync(Ticket, Cuit);
        Assert.Equal("smallTaxpayer", person?.TaxStatus);

        var ambiguous = new ArcaRegistryClient(new HttpClient(new Handler(Response(
            "<datosRegimenGeneral><impuesto><idImpuesto>30</idImpuesto>" +
            "<estadoImpuesto>AC</estadoImpuesto></impuesto></datosRegimenGeneral>" +
            "<datosMonotributo><impuesto><idImpuesto>20</idImpuesto>" +
            "<estadoImpuesto>AC</estadoImpuesto></impuesto></datosMonotributo>"))));
        Assert.Null((await ambiguous.GetPersonAsync(Ticket, Cuit))?.TaxStatus);
    }

    [Fact]
    public async Task RejectsForeignCuitAndUnsafeXmlWithoutExposingResponse()
    {
        var foreign = new ArcaRegistryClient(new HttpClient(new Handler(Response("", "20999999999"))));
        Assert.Null(await foreign.GetPersonAsync(Ticket, Cuit));

        var unsafeClient = new ArcaRegistryClient(new HttpClient(new Handler(
            "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///C:/secret'>]><x>&secret;</x>")));
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            unsafeClient.GetPersonAsync(Ticket, Cuit));
        Assert.DoesNotContain("C:/secret", error.ToString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("20164755100", true)]
    [InlineData("20164755101", false)]
    [InlineData("20-16475510-0", false)]
    public void ValidatesCuitDigit(string? cuit, bool expected) =>
        Assert.Equal(expected, FiscalCuit.IsValid(cuit));

    private static string Response(string tax, string? cuit = null) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><getPersona_v2Response><personaReturn>
            <datosGenerales><idPersona>{cuit ?? Cuit}</idPersona><estadoClave>ACTIVO</estadoClave>
              <apellido>GARCIA</apellido><nombre>ANA</nombre>
              <domicilioFiscal><direccion>SAN MANUEL 9</direccion><localidad>SAN MANUEL</localidad>
                <descripcionProvincia>BUENOS AIRES</descripcionProvincia></domicilioFiscal>
            </datosGenerales>{tax}</personaReturn></getPersona_v2Response></soap:Body>
        </soap:Envelope>
        """;

    private sealed class Handler(string response) : HttpMessageHandler
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
