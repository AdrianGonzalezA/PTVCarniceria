using System.Security.Cryptography.X509Certificates;

namespace Carnicerias.Api.Fiscal;

/// <summary>In-process WSAA ticket cache. Credentials remain outside the repository.</summary>
public sealed class ArcaHomologationTicketProvider(
    ArcaWsaaClient wsaa, IConfiguration configuration, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ArcaAccessTicket? _ticket;

    public string IssuerCuit => configuration["ARCA_HOMO_CUIT"] ?? string.Empty;
    public int PointOfSale => int.TryParse(configuration["ARCA_HOMO_POINT_OF_SALE"],
        out var number) ? number : 0;
    public string IssuerName => configuration["ARCA_HOMO_ISSUER_NAME"] ?? string.Empty;
    public string IssuerAddress => configuration["ARCA_HOMO_ISSUER_ADDRESS"] ?? string.Empty;
    public string? IssuerIibb => string.IsNullOrWhiteSpace(configuration["ARCA_HOMO_ISSUER_IIBB"])
        ? null : configuration["ARCA_HOMO_ISSUER_IIBB"];
    public string? IssuerActivityStartDate => string.IsNullOrWhiteSpace(configuration["ARCA_HOMO_ISSUER_ACTIVITY_START_DATE"])
        ? null : configuration["ARCA_HOMO_ISSUER_ACTIVITY_START_DATE"];
    public Guid CompanyId => Guid.TryParse(configuration["ARCA_HOMO_COMPANY_ID"],
        out var id) ? id : Guid.Empty;

    public bool IsConfigured => IssuerCuit.Length == 11 && IssuerCuit.All(char.IsAsciiDigit) &&
        PointOfSale is > 0 and < 99999 && CompanyId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(IssuerName) && !string.IsNullOrWhiteSpace(IssuerAddress) &&
        File.Exists(configuration["ARCA_HOMO_PFX_PATH"]);

    public async Task<ArcaAccessTicket> GetAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ARCA_HOMO_NOT_CONFIGURED");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ticket?.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(5))
                return _ticket;
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                configuration["ARCA_HOMO_PFX_PATH"]!, configuration["ARCA_HOMO_PFX_PASSWORD"],
                X509KeyStorageFlags.EphemeralKeySet);
            _ticket = await wsaa.RequestTicketAsync(certificate, IssuerCuit, cancellationToken);
            return _ticket;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
