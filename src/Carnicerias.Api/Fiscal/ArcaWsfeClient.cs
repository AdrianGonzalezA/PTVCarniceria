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
    DateOnly? AuthorizationExpiry, int ReceiverDocumentType, long ReceiverDocumentNumber,
    DateOnly IssueDate);

public sealed record ArcaPointOfSale(int Number, string IssuanceType, bool IsBlocked,
    DateOnly? DeactivatedOn);

public sealed class ArcaWsfeErrorException(IReadOnlyList<int> codes)
    : Exception($"WSFE error codes: {string.Join(",", codes)}")
{
    public IReadOnlyList<int> Codes { get; } = codes.ToArray();
}

public sealed class ArcaWsfeRejectionException(IReadOnlyList<int> codes)
    : Exception($"WSFE rejected the invoice with codes: {string.Join(",", codes)}")
{
    public IReadOnlyList<int> Codes { get; } = codes.ToArray();
}

public sealed record ArcaVatAmount(int ArcaRateCode, decimal TaxableBase, decimal TaxAmount);

public sealed class ArcaCaeRequest
{
    public ArcaCaeRequest(int pointOfSale, int voucherType, long number, DateOnly issueDate,
        int receiverDocumentType, long receiverDocumentNumber, int receiverVatConditionCode,
        decimal total, decimal taxableBase, decimal exemptAmount, decimal notTaxedAmount,
        IReadOnlyList<ArcaVatAmount> vatAmounts)
    {
        ArgumentNullException.ThrowIfNull(vatAmounts);
        if (pointOfSale <= 0 || voucherType <= 0 || number <= 0 || issueDate == default ||
            receiverDocumentType <= 0 || receiverDocumentNumber < 0 || receiverVatConditionCode <= 0 ||
            !Money(total) || total <= 0 || !Money(taxableBase) || !Money(exemptAmount) ||
            !Money(notTaxedAmount) || vatAmounts.Count > 20 ||
            vatAmounts.Any(item => item.ArcaRateCode <= 0 || !Money(item.TaxableBase) ||
                !Money(item.TaxAmount) || item.TaxableBase <= 0 || item.TaxAmount < 0) ||
            vatAmounts.Select(item => item.ArcaRateCode).Distinct().Count() != vatAmounts.Count ||
            vatAmounts.Sum(item => item.TaxableBase) != taxableBase ||
            taxableBase + exemptAmount + notTaxedAmount + vatAmounts.Sum(item => item.TaxAmount) != total)
            throw new ArgumentException("An explicit, balanced fiscal request is required.");
        PointOfSale = pointOfSale;
        VoucherType = voucherType;
        Number = number;
        IssueDate = issueDate;
        ReceiverDocumentType = receiverDocumentType;
        ReceiverDocumentNumber = receiverDocumentNumber;
        ReceiverVatConditionCode = receiverVatConditionCode;
        Total = total;
        TaxableBase = taxableBase;
        ExemptAmount = exemptAmount;
        NotTaxedAmount = notTaxedAmount;
        VatAmounts = vatAmounts.ToArray();
    }

    public int PointOfSale { get; }
    public int VoucherType { get; }
    public long Number { get; }
    public DateOnly IssueDate { get; }
    public int ReceiverDocumentType { get; }
    public long ReceiverDocumentNumber { get; }
    public int ReceiverVatConditionCode { get; }
    public decimal Total { get; }
    public decimal TaxableBase { get; }
    public decimal ExemptAmount { get; }
    public decimal NotTaxedAmount { get; }
    public IReadOnlyList<ArcaVatAmount> VatAmounts { get; }

    private static bool Money(decimal amount) => amount >= 0 && amount <= 9_999_999_999.99m &&
        decimal.Round(amount, 2) == amount;
}

public sealed record ArcaCaeResult(int PointOfSale, int VoucherType, long Number,
    string Cae, DateOnly Expiry);

/// <summary>
/// WSFEv1 homologation-only adapter. The WSAA ticket must be supplied by a separate
/// credential provider. Issuance is not exposed to the POS and must not be blindly retried.
/// </summary>
public sealed class ArcaWsfeClient(HttpClient httpClient)
{
    private const string Endpoint = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Wsfe = "http://ar.gov.afip.dif.FEV1/";

    public async Task<IReadOnlyList<ArcaPointOfSale>> GetPointsOfSaleAsync(
        ArcaAccessTicket ticket, CancellationToken cancellationToken = default)
    {
        ValidateTicket(ticket);
        var result = await SendAsync("FEParamGetPtosVenta", ticket, [], cancellationToken);
        var entries = Child(result, "ResultGet")?.Elements()
            .Where(element => element.Name.LocalName == "PtoVenta").ToArray() ?? [];
        if (entries.Length > 100_000)
            throw new InvalidDataException("WSFE returned too many points of sale.");
        var points = new List<ArcaPointOfSale>(entries.Length);
        foreach (var entry in entries)
        {
            var number = ReadInt(entry, "Nro");
            var issuanceType = ReadText(entry, "EmisionTipo");
            var blocked = ReadText(entry, "Bloqueado");
            var deactivatedText = Child(entry, "FchBaja")?.Value.Trim();
            DateOnly? deactivated = null;
            if (!string.IsNullOrWhiteSpace(deactivatedText))
            {
                if (!DateOnly.TryParseExact(deactivatedText, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                    throw new InvalidDataException("WSFE returned an invalid deactivation date.");
                deactivated = parsed;
            }
            if (number is < 1 or > 99998 || issuanceType.Length > 8 ||
                blocked is not ("S" or "N") || points.Any(point => point.Number == number))
                throw new InvalidDataException("WSFE returned an invalid point of sale.");
            points.Add(new ArcaPointOfSale(number, issuanceType, blocked == "S", deactivated));
        }
        return points;
    }

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
        var receiverDocumentType = ReadInt(invoice, "DocTipo");
        var receiverDocumentNumber = ReadLong(invoice, "DocNro");
        var issueDateText = ReadText(invoice, "CbteFch");
        if (ReadInt(invoice, "PtoVta") != pointOfSale ||
            ReadInt(invoice, "CbteTipo") != voucherType ||
            ReadLong(invoice, "CbteDesde") != number ||
            ReadLong(invoice, "CbteHasta") != number || total <= 0 ||
            resultCode is not ("A" or "R") ||
            kind is not ("CAE" or "CAEA") || authorization.Length is < 1 or > 40 ||
            receiverDocumentType <= 0 || receiverDocumentNumber < 0 ||
            !DateOnly.TryParseExact(issueDateText, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var issueDate))
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
            total, resultCode, authorization, kind, expiry,
            receiverDocumentType, receiverDocumentNumber, issueDate);
    }

    public async Task<ArcaCaeResult> RequestCaeAsync(ArcaAccessTicket ticket,
        ArcaCaeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(ticket, request.PointOfSale, request.VoucherType);
        var details = new XElement(Wsfe + "FECAEDetRequest",
            new XElement(Wsfe + "Concepto", 1),
            new XElement(Wsfe + "DocTipo", request.ReceiverDocumentType),
            new XElement(Wsfe + "DocNro", request.ReceiverDocumentNumber),
            new XElement(Wsfe + "CbteDesde", request.Number),
            new XElement(Wsfe + "CbteHasta", request.Number),
            new XElement(Wsfe + "CbteFch", request.IssueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new XElement(Wsfe + "ImpTotal", Amount(request.Total)),
            new XElement(Wsfe + "ImpTotConc", Amount(request.NotTaxedAmount)),
            new XElement(Wsfe + "ImpNeto", Amount(request.TaxableBase)),
            new XElement(Wsfe + "ImpOpEx", Amount(request.ExemptAmount)),
            new XElement(Wsfe + "ImpTrib", 0),
            new XElement(Wsfe + "ImpIVA", Amount(request.VatAmounts.Sum(item => item.TaxAmount))),
            new XElement(Wsfe + "MonId", "PES"),
            new XElement(Wsfe + "MonCotiz", 1),
            new XElement(Wsfe + "CondicionIVAReceptorId", request.ReceiverVatConditionCode));
        if (request.VatAmounts.Count > 0)
            details.Add(new XElement(Wsfe + "Iva", request.VatAmounts.Select(item =>
                new XElement(Wsfe + "AlicIva",
                    new XElement(Wsfe + "Id", item.ArcaRateCode),
                    new XElement(Wsfe + "BaseImp", Amount(item.TaxableBase)),
                    new XElement(Wsfe + "Importe", Amount(item.TaxAmount))))));
        var result = await SendAsync("FECAESolicitar", ticket,
            [new XElement(Wsfe + "FeCAEReq",
                new XElement(Wsfe + "FeCabReq",
                    new XElement(Wsfe + "CantReg", 1),
                    new XElement(Wsfe + "PtoVta", request.PointOfSale),
                    new XElement(Wsfe + "CbteTipo", request.VoucherType)),
                new XElement(Wsfe + "FeDetReq", details))], cancellationToken);
        var header = Child(result, "FeCabResp")
            ?? throw new InvalidDataException("WSFE omitted the CAE response header.");
        var line = Child(Child(result, "FeDetResp")
            ?? throw new InvalidDataException("WSFE omitted the CAE response detail."), "FECAEDetResponse")
            ?? throw new InvalidDataException("WSFE omitted the CAE response line.");
        if (ReadInt(header, "PtoVta") != request.PointOfSale ||
            ReadInt(header, "CbteTipo") != request.VoucherType ||
            ReadInt(header, "CantReg") != 1 ||
            ReadLong(line, "CbteDesde") != request.Number ||
            ReadLong(line, "CbteHasta") != request.Number)
            throw new InvalidDataException("WSFE returned a mismatched invoice response.");
        if (ReadText(line, "Resultado") == "R")
        {
            var observations = Child(line, "Observaciones")?.Elements()
                .Where(element => element.Name.LocalName == "Obs").ToArray() ?? [];
            if (observations.Length is < 1 or > 100)
                throw new InvalidDataException("WSFE rejected the invoice without bounded observation codes.");
            var codes = observations.Select(item => ReadInt(item, "Code")).ToArray();
            if (codes.Any(code => code is < 1 or > 999999))
                throw new InvalidDataException("WSFE returned invalid rejection codes.");
            throw new ArcaWsfeRejectionException(codes);
        }
        var cae = ReadText(line, "CAE");
        var expiryText = ReadText(line, "CAEFchVto");
        if (ReadText(line, "Resultado") != "A" ||
            cae.Length != 14 || !cae.All(char.IsAsciiDigit) ||
            !DateOnly.TryParseExact(expiryText, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry))
            throw new InvalidDataException("WSFE did not authorize the requested invoice.");
        return new ArcaCaeResult(request.PointOfSale, request.VoucherType, request.Number,
            cae, expiry);
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
        if (Child(result, "Errors") is { } errors)
        {
            var rows = errors.Elements().Where(element => element.Name.LocalName == "Err").ToArray();
            if (rows.Length > 100)
                throw new InvalidDataException("WSFE returned too many errors.");
            if (rows.Length > 0)
            {
                var codes = rows.Select(row => ReadInt(row, "Code")).ToArray();
                if (codes.Any(code => code is < 1 or > 999_999))
                    throw new InvalidDataException("WSFE returned invalid error codes.");
                throw new ArcaWsfeErrorException(codes);
            }
        }
        return result;
    }

    private static void Validate(ArcaAccessTicket ticket, int pointOfSale, int voucherType)
    {
        ValidateTicket(ticket);
        if (pointOfSale <= 0 || voucherType <= 0)
            throw new ArgumentException("A valid point of sale and voucher type are required.");
    }

    private static void ValidateTicket(ArcaAccessTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(ticket.Token) || string.IsNullOrWhiteSpace(ticket.Sign) ||
            ticket.Cuit is null || ticket.Cuit.Length != 11 || !ticket.Cuit.All(char.IsAsciiDigit))
            throw new ArgumentException("A valid WSAA ticket and CUIT are required.");
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

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
