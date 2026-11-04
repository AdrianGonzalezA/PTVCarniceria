using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Carnicerias.Api.Fiscal;

public sealed record ArcaRegistryPerson(string Cuit, string? Name, string? Address,
    string? Locality, string? Province, string? TaxStatus);

/// <summary>Read-only CUIT lookup in ARCA homologation; never changes customer records.</summary>
public sealed class ArcaRegistryClient(HttpClient httpClient)
{
    private const string Endpoint = "https://awshomo.arca.gob.ar/sr-padron/webservices/personaServiceA5";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Registry = "http://a5.soap.ws.server.puc.sr/";

    public async Task<ArcaRegistryPerson?> GetPersonAsync(ArcaAccessTicket ticket,
        string cuit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(ticket.Token) || string.IsNullOrWhiteSpace(ticket.Sign) ||
            !ValidCuit(ticket.Cuit) || !ValidCuit(cuit))
            throw new ArgumentException("A valid registry ticket and CUIT are required.");

        var envelope = new XDocument(new XElement(Soap + "Envelope",
            new XElement(Soap + "Body", new XElement(Registry + "getPersona_v2",
                new XElement("token", ticket.Token), new XElement("sign", ticket.Sign),
                new XElement("cuitRepresentada", ticket.Cuit), new XElement("idPersona", cuit)))));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8, "text/xml")
        };
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 1_000_000
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        if (document.Descendants().Any(item => item.Name.LocalName == "Fault"))
            throw new InvalidDataException("ARCA registry returned a SOAP fault.");
        response.EnsureSuccessStatusCode();
        var body = document.Root?.Elements().SingleOrDefault(item => item.Name.LocalName == "Body");
        var result = body?.Elements().SingleOrDefault(item => item.Name.LocalName == "getPersona_v2Response")
            ?.Elements().SingleOrDefault(item => item.Name.LocalName == "personaReturn")
            ?? throw new InvalidDataException("ARCA registry response is incomplete.");
        if (Child(result, "errorConstancia") is not null) return null;
        var general = Child(result, "datosGenerales");
        if (general is null || Read(general, "idPersona") != cuit ||
            Read(general, "estadoClave") != "ACTIVO") return null;

        var address = Child(general, "domicilioFiscal");
        var name = Read(general, "razonSocial") ??
            Join(Read(general, "apellido"), Read(general, "nombre"));
        var registered = HasActiveTax(Child(result, "datosRegimenGeneral"), "30");
        var smallTaxpayer = HasActiveTax(Child(result, "datosMonotributo"), "20");
        var status = Child(result, "errorRegimenGeneral") is not null ||
            Child(result, "errorMonotributo") is not null || registered == smallTaxpayer
                ? null : registered ? "registered" : "smallTaxpayer";
        return new ArcaRegistryPerson(cuit, Bounded(name, 200),
            Bounded(Read(address, "direccion"), 200),
            Bounded(Read(address, "localidad"), 100),
            Bounded(Read(address, "descripcionProvincia"), 100), status);
    }

    private static bool HasActiveTax(XElement? group, string id) =>
        group?.Elements().Any(item => item.Name.LocalName == "impuesto" &&
            Read(item, "idImpuesto") == id && Read(item, "estadoImpuesto") == "AC") == true;

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().SingleOrDefault(item => item.Name.LocalName == name);

    private static string? Read(XElement? parent, string name) =>
        Child(parent, name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static string? Join(string? first, string? second) =>
        string.Join(' ', new[] { first, second }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private static string? Bounded(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum ? value : null;

    private static bool ValidCuit(string value) => value.Length == 11 && value.All(char.IsAsciiDigit);
}
