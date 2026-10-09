using System.Security.Cryptography.X509Certificates;
using Carnicerias.Api.Fiscal;

var pfxPath = Environment.GetEnvironmentVariable("ARCA_HOMO_PFX_PATH");
var cuit = Environment.GetEnvironmentVariable("ARCA_HOMO_CUIT");
var password = Environment.GetEnvironmentVariable("ARCA_HOMO_PFX_PASSWORD");
if (string.IsNullOrWhiteSpace(pfxPath) || !File.Exists(pfxPath) ||
    !string.Equals(Path.GetExtension(pfxPath), ".pfx", StringComparison.OrdinalIgnoreCase) ||
    cuit is not { Length: 11 } || !cuit.All(char.IsAsciiDigit))
{
    Console.Error.WriteLine("Configure ARCA_HOMO_PFX_PATH and ARCA_HOMO_CUIT for a local homologation probe.");
    return 2;
}

var stage = "WSAA";
try
{
    // EphemeralKeySet avoids importing the private key into the operating-system key store.
    // Source: https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509keystorageflags
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
        pfxPath, password, X509KeyStorageFlags.EphemeralKeySet);
    if (!certificate.HasPrivateKey)
        throw new InvalidDataException("The PFX has no private key.");
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    var ticket = await new ArcaWsaaClient(httpClient, TimeProvider.System)
        .RequestTicketAsync(certificate, cuit);
    Console.WriteLine("WSAA homologation: authenticated.");
    stage = "WSFE";
    var wsfe = new ArcaWsfeClient(httpClient);
    var pointOfSaleText = Environment.GetEnvironmentVariable("ARCA_HOMO_POINT_OF_SALE");
    if (int.TryParse(pointOfSaleText, out var pointOfSale) && pointOfSale is > 0 and < 99999)
    {
        foreach (var voucherType in new[] { 1, 6 })
        {
            var last = await wsfe.GetLastAuthorizedAsync(ticket, pointOfSale, voucherType);
            Console.WriteLine($"WSFE homologation point {pointOfSale}, type {voucherType}: last authorized {last}.");
        }
    }
    else
    {
        var points = await wsfe.GetPointsOfSaleAsync(ticket);
        Console.WriteLine($"WSFE homologation: {points.Count} fiscal point(s) returned.");
        foreach (var point in points.Take(20))
            Console.WriteLine($"Point {point.Number}: {point.IssuanceType}, blocked={point.IsBlocked}, deactivated={point.DeactivatedOn is not null}");
    }
    return 0;
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    // Never dump provider responses, SOAP, tickets or certificate details.
    var detail = exception switch
    {
        ArcaWsaaFaultException fault => $"SOAP fault {fault.Code}",
        ArcaWsfeErrorException wsfeError => $"WSFE code(s) {string.Join(",", wsfeError.Codes)}",
        HttpRequestException httpError => $"HTTP {(int?)httpError.StatusCode ?? 0}",
        InvalidDataException invalidData => invalidData.Message,
        _ => exception.GetType().Name
    };
    Console.Error.WriteLine($"ARCA homologation {stage} probe failed: {detail}.");
    return 1;
}
