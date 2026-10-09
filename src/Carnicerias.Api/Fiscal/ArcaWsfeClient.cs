using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Carnicerias.Api.Fiscal;

public sealed record ArcaAccessTicket(string Token, string Sign, string Cuit,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public override string ToString() => "ArcaAccessTicket [redacted]";
}

public sealed record ArcaInvoiceLookup(int PointOfSale, int VoucherType, long Number,
    decimal Total, string Result, string AuthorizationCode, string AuthorizationKind,
    DateOnly? AuthorizationExpiry);

/// <summary>
/// Read-only WSFEv1 homologation adapter. The WSAA ticket must be supplied by a separate
/// credential provider. It never creates a CAE or calls the production endpoint.
/// </summary>
public sealed class ArcaWsfeClient(HttpClient httpClient)
{
    private const string Endpoint = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Wsfe = "http://ar.gov.afip.dif.FEV1/";

    public async Task<long> GetLastAuthorizedAsync(ArcaAccessTicket ticket,
        int pointOfSale, int voucherType, CancellationToken cancellationToken = default)
    {
        Validate(ticket, pointOfSale, voucherType);
        var result = await SendAsync("FECompUltimoAutorizado", ticket,
            [new XElement(Wsfe + "PtoVta", pointOfSale),
                new XElement(Wsfe + "CbteTipo", voucherType)], cancellationToken);
        var number = ReadLong(result, "CbteNro");
        if (ReadInt(result, "PtoVta") != pointOfSale ||
            ReadInt(result, "CbteTipo") != voucherType || number < 0)
            throw new InvalidDataException("WSFE returned a mismatched last-authorized response.");
        return number;
    }

    public async Task<ArcaInvoiceLookup> ConsultAsync(ArcaAccessTicket ticket,
        int pointOfSale, int voucherType, long number, CancellationToken cancellationToken = default)
    {
        Validate(ticket, pointOfSale, voucherType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        var result = await SendAsync("FECompConsultar", ticket,
            [new XElement(Wsfe + "FeCompConsReq",
                new XElement(Wsfe + "CbteTipo", voucherType),
                new XElement(Wsfe + "CbteNro", number),
                new XElement(Wsfe + "PtoVta", pointOfSale))], cancellationToken);
        var invoice = Child(result, "ResultGet")
            ?? throw new InvalidDataException("WSFE did not return the requested invoice.");
        var total = ReadDecimal(invoice, "ImpTotal");
        var resultCode = ReadText(invoice, "Resultado");
        var authorization = ReadText(invoice, "CodAutorizacion");
        var kind = ReadText(invoice, "EmisionTipo");
        if (ReadInt(invoice, "PtoVta") != pointOfSale ||
            ReadInt(invoice, "CbteTipo") != voucherType ||
            ReadLong(invoice, "CbteDesde") != number ||
            ReadLong(invoice, "CbteHasta") != number || total <= 0 ||
            resultCode is not ("A" or "R") ||
            kind is not ("CAE" or "CAEA") || authorization.Length is < 1 or > 40)
            throw new InvalidDataException("WSFE returned a mismatched invoice response.");
        var expiryText = Child(invoice, "FchVto")?.Value;
        DateOnly? expiry = null;
        if (!string.IsNullOrWhiteSpace(expiryText))
        {
            if (!DateOnly.TryParseExact(expiryText, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedExpiry))
                throw new InvalidDataException("WSFE returned an invalid authorization expiry.");
            expiry = parsedExpiry;
        }
        return new ArcaInvoiceLookup(pointOfSale, voucherType, number,
            total, resultCode, authorization, kind, expiry);
    }

    private async Task<XElement> SendAsync(string operation, ArcaAccessTicket ticket,
        IEnumerable<XElement> arguments, CancellationToken cancellationToken)
    {
        var envelope = new XDocument(new XElement(Soap + "Envelope",
            new XElement(Soap + "Body", new XElement(Wsfe + operation,
                new XElement(Wsfe + "Auth",
                    new XElement(Wsfe + "Token", ticket.Token),
                    new XElement(Wsfe + "Sign", ticket.Sign),
                    new XElement(Wsfe + "Cuit", ticket.Cuit)), arguments))));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{Wsfe}{operation}\"");
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        if (document.Descendants().Any(element => element.Name.LocalName == "Fault"))
            throw new InvalidDataException("WSFE returned a SOAP fault; reconcile before retrying.");
        var result = document.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == $"{operation}Result")
            ?? throw new InvalidDataException("WSFE returned an unexpected SOAP response.");
        if (Child(result, "Errors") is not null)
            throw new InvalidDataException("WSFE rejected the consultation; reconcile before retrying.");
        return result;
    }

    private static void Validate(ArcaAccessTicket ticket, int pointOfSale, int voucherType)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(ticket.Token) || string.IsNullOrWhiteSpace(ticket.Sign) ||
            ticket.Cuit is null || ticket.Cuit.Length != 11 || !ticket.Cuit.All(char.IsAsciiDigit) ||
            pointOfSale <= 0 || voucherType <= 0)
            throw new ArgumentException("A valid WSAA ticket, CUIT, point of sale and voucher type are required.");
    }

    private static XElement? Child(XElement source, string name) =>
        source.Elements().SingleOrDefault(element => element.Name.LocalName == name);

    private static string ReadText(XElement source, string name) =>
        Child(source, name)?.Value.Trim() is { Length: > 0 } value
            ? value : throw new InvalidDataException($"WSFE omitted {name}.");

    private static int ReadInt(XElement source, string name) =>
        int.TryParse(ReadText(source, name), NumberStyles.None, CultureInfo.InvariantCulture,
            out var value) ? value : throw new InvalidDataException($"WSFE returned invalid {name}.");

    private static long ReadLong(XElement source, string name) =>
        long.TryParse(ReadText(source, name), NumberStyles.None, CultureInfo.InvariantCulture,
            out var value) ? value : throw new InvalidDataException($"WSFE returned invalid {name}.");

    private static decimal ReadDecimal(XElement source, string name) =>
        decimal.TryParse(ReadText(source, name), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidDataException($"WSFE returned invalid {name}.");
}
