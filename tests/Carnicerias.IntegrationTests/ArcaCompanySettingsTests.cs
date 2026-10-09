using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Carnicerias.Api.Fiscal;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ArcaCompanySettingsTests
{
    [Fact]
    public void CertificateInspectionReturnsValidityWithoutPrivateMaterial()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Homologacion de prueba", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));

        var result = ArcaCertificateInspector.Inspect(certificate.Export(X509ContentType.Pkcs12),
            null, DateTimeOffset.UtcNow);

        Assert.Contains("Homologacion de prueba", result.Subject);
        Assert.True(result.NotAfterUtc > DateTimeOffset.UtcNow.AddDays(20));
        Assert.Equal(40, result.Thumbprint.Length);
    }

    [Fact]
    public void CertificateInspectionRejectsInvalidAndExpiredPfx()
    {
        Assert.Throws<CryptographicException>(() => ArcaCertificateInspector.Inspect(
            [1, 2, 3], null, DateTimeOffset.UtcNow));

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Vencido", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Throws<CryptographicException>(() => ArcaCertificateInspector.Inspect(
            expired.Export(X509ContentType.Pkcs12), null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SettingsRejectInvalidIssuerAndPreserveCertificateOnMetadataUpdate()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new ArcaCompanySettings(companyId,
            "123", 99, "Emisor", "Domicilio", null, null, userId, now));

        var settings = new ArcaCompanySettings(companyId, "30710106513", 99,
            "Emisor", "Domicilio", null, null, userId, now);
        settings.AssignCertificate([1, 2, 3], null, "CN=Prueba", new string('A', 40),
            now.AddDays(-1), now.AddDays(30), userId, now);
        settings.Update("30710106513", 98, "Otro emisor", "Otro domicilio",
            "123", new DateOnly(2020, 1, 1), userId, now.AddMinutes(1));

        Assert.Equal(98, settings.PointOfSale);
        Assert.Equal("Otro emisor", settings.IssuerName);
        Assert.Equal(new byte[] { 1, 2, 3 }, settings.ProtectedPfx);
        Assert.Equal(now.AddDays(30), settings.CertificateNotAfterUtc);
    }
}
