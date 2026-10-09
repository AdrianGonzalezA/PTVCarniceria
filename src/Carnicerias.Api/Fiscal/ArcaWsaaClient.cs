using System.Globalization;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Carnicerias.Api.Fiscal;

public sealed class ArcaWsaaFaultException(string code) : Exception($"WSAA fault: {code}")
{
    public string Code { get; } = code;
}

/// <summary>Requests a WSAA access ticket for wsfe in homologation only.</summary>
public sealed class ArcaWsaaClient(HttpClient httpClient, TimeProvider timeProvider)
{
    private const string Endpoint = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    public async Task<ArcaAccessTicket> RequestTicketAsync(X509Certificate2 certificate,
        string cuit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey || string.IsNullOrWhiteSpace(cuit) ||
            cuit.Length != 11 || !cuit.All(char.IsAsciiDigit))
            throw new ArgumentException("A certificate with private key and issuer CUIT are required.");
        var now = timeProvider.GetUtcNow();
        if (now < certificate.NotBefore || now >= certificate.NotAfter)
            throw new ArgumentException("The WSAA certificate is not currently valid.", nameof(certificate));

        var requestXml = new XElement("loginTicketRequest",
            new XAttribute("version", "1.0"),
            new XElement("header",
                new XElement("uniqueId", unchecked((uint)now.ToUnixTimeSeconds())),
                new XElement("generationTime", now.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture)),
                new XElement("expirationTime", now.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture))),
            new XElement("service", "wsfe"));
        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(
            requestXml.ToString(SaveOptions.DisableFormatting))));
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly
        };
        cms.ComputeSignature(signer);
        var envelope = new XDocument(new XElement(Soap + "Envelope",
            new XElement(Soap + "Body", new XElement(Wsaa + "loginCms",
                new XElement(Wsaa + "in0", Convert.ToBase64String(cms.Encode()))))));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", "urn:LoginCms");
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.Content.LoadIntoBufferAsync(1_000_000, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, SafeXmlSettings());
        var outer = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        var fault = outer.Descendants().SingleOrDefault(element => element.Name.LocalName == "Fault");
        if (fault is not null)
        {
            var suppliedCode = fault.Elements().SingleOrDefault(element =>
                element.Name.LocalName is "faultcode" or "Code")?.Value.Trim();
            var safeCode = suppliedCode is { Length: > 0 and <= 80 } &&
                suppliedCode.All(character => char.IsAsciiLetterOrDigit(character) ||
                    character is '.' or '_' or '-' or ':') ? suppliedCode : "unclassified";
            throw new ArcaWsaaFaultException(safeCode);
        }
        response.EnsureSuccessStatusCode();
        var ticketXml = outer.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "loginCmsReturn")?.Value;
        if (string.IsNullOrWhiteSpace(ticketXml) || ticketXml.Length > 1_000_000)
            throw new InvalidDataException("WSAA did not return a bounded ticket.");
        using var ticketReader = XmlReader.Create(new StringReader(ticketXml), SafeXmlSettings());
        var ticket = await XDocument.LoadAsync(ticketReader, LoadOptions.None, cancellationToken);
        var credentials = ticket.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "credentials")
            ?? throw new InvalidDataException("WSAA omitted ticket credentials.");
        var token = Read(credentials, "token");
        var sign = Read(credentials, "sign");
        var expiryText = ticket.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "expirationTime")?.Value;
        if (!DateTimeOffset.TryParse(expiryText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var expiry) ||
            expiry <= now.AddMinutes(1) || expiry > now.AddDays(1))
            throw new InvalidDataException("WSAA returned an expired or implausible ticket.");
        var service = ticket.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "service")?.Value;
        if (service is not null && service != "wsfe")
            throw new InvalidDataException("WSAA returned a ticket for another service.");
        return new ArcaAccessTicket(token, sign, cuit, expiry.ToUniversalTime());
    }

    private static XmlReaderSettings SafeXmlSettings() => new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1_000_000
    };

    private static string Read(XElement element, string name) =>
        element.Elements().SingleOrDefault(child => child.Name.LocalName == name)?.Value
            is { Length: > 0 } value
            ? value : throw new InvalidDataException($"WSAA omitted {name}.");
}
