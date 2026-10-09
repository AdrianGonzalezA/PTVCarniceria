using System.Security.Cryptography.X509Certificates;
using Carnicerias.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Fiscal;

public sealed record ArcaRuntimeSettings(Guid CompanyId, string IssuerCuit, int PointOfSale,
    string IssuerName, string IssuerAddress, string? IssuerIibb, string? IssuerActivityStartDate,
    byte[]? Pfx, string? PfxPath, string? Password, string? CertificateThumbprint)
{
    public bool IsConfigured => CompanyId != Guid.Empty && IssuerCuit.Length == 11 &&
        IssuerCuit.All(char.IsAsciiDigit) && PointOfSale is > 0 and < 99999 &&
        !string.IsNullOrWhiteSpace(IssuerName) && !string.IsNullOrWhiteSpace(IssuerAddress) &&
        (Pfx is { Length: > 0 } || PfxPath is not null && File.Exists(PfxPath));

    public X509Certificate2 LoadCertificate() => Pfx is { Length: > 0 }
        ? X509CertificateLoader.LoadPkcs12(Pfx, Password, X509KeyStorageFlags.EphemeralKeySet)
        : X509CertificateLoader.LoadPkcs12FromFile(PfxPath!, Password,
            X509KeyStorageFlags.EphemeralKeySet);
}

public sealed class ArcaSettingsResolver(PlatformAccessDbContext db,
    IDataProtectionProvider dataProtection, IConfiguration configuration)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Carnicerias.Arca.Pfx.v1");

    public byte[] Protect(byte[] value) => _protector.Protect(value);

    public async Task<ArcaRuntimeSettings> ResolveAsync(Guid companyId, CancellationToken cancellationToken,
        bool includeCertificate = true)
    {
        var settings = await db.ArcaCompanySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        if (settings is not null)
            return new ArcaRuntimeSettings(companyId, settings.IssuerCuit, settings.PointOfSale,
                settings.IssuerName, settings.IssuerAddress, settings.IssuerIibb,
                settings.IssuerActivityStartDate?.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                !includeCertificate || settings.ProtectedPfx is null ? null :
                    _protector.Unprotect(settings.ProtectedPfx),
                null, !includeCertificate || settings.ProtectedPassword is null ? null :
                    System.Text.Encoding.UTF8.GetString(_protector.Unprotect(settings.ProtectedPassword)),
                settings.CertificateThumbprint);
        var configuredCompany = Guid.TryParse(configuration["ARCA_HOMO_COMPANY_ID"], out var id) &&
            id == companyId;
        return new ArcaRuntimeSettings(companyId, configuredCompany ? configuration["ARCA_HOMO_CUIT"] ?? "" : "",
            configuredCompany && int.TryParse(configuration["ARCA_HOMO_POINT_OF_SALE"], out var pointOfSale)
                ? pointOfSale : 0,
            configuredCompany ? configuration["ARCA_HOMO_ISSUER_NAME"] ?? "" : "",
            configuredCompany ? configuration["ARCA_HOMO_ISSUER_ADDRESS"] ?? "" : "",
            configuredCompany ? configuration["ARCA_HOMO_ISSUER_IIBB"] : null,
            configuredCompany ? configuration["ARCA_HOMO_ISSUER_ACTIVITY_START_DATE"] : null,
            null, includeCertificate && configuredCompany ? configuration["ARCA_HOMO_PFX_PATH"] : null,
            includeCertificate && configuredCompany ? configuration["ARCA_HOMO_PFX_PASSWORD"] : null, null);
    }
}
