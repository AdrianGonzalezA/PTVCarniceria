using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Carnicerias.Api.Fiscal;

public sealed record ArcaCertificateDetails(string Subject, string Thumbprint,
    DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc);

public static class ArcaCertificateInspector
{
    public const int MaxPfxBytes = 1024 * 1024;

    public static ArcaCertificateDetails Inspect(byte[] pfx, string? password, DateTimeOffset now)
    {
        if (pfx.Length is 0 or > MaxPfxBytes)
            throw new ArgumentException("PFX size is invalid.", nameof(pfx));
        using var certificate = X509CertificateLoader.LoadPkcs12(pfx, password,
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime ||
            certificate.NotBefore.ToUniversalTime() > now.UtcDateTime)
            throw new CryptographicException("The PFX has no usable private key or is not currently valid.");
        return new ArcaCertificateDetails(certificate.Subject, certificate.Thumbprint,
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
    }
}
