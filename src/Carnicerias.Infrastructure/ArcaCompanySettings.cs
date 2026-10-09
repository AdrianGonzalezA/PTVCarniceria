namespace Carnicerias.Infrastructure;

public sealed class ArcaCompanySettings
{
    private ArcaCompanySettings() { }

    public ArcaCompanySettings(Guid companyId, string issuerCuit, int pointOfSale,
        string issuerName, string issuerAddress, string? issuerIibb,
        DateOnly? issuerActivityStartDate, Guid updatedByUserId, DateTimeOffset updatedAtUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.");
        CompanyId = companyId;
        Update(issuerCuit, pointOfSale, issuerName, issuerAddress, issuerIibb,
            issuerActivityStartDate, updatedByUserId, updatedAtUtc);
    }

    public Guid CompanyId { get; private set; }
    public string IssuerCuit { get; private set; } = string.Empty;
    public int PointOfSale { get; private set; }
    public string IssuerName { get; private set; } = string.Empty;
    public string IssuerAddress { get; private set; } = string.Empty;
    public string? IssuerIibb { get; private set; }
    public DateOnly? IssuerActivityStartDate { get; private set; }
    public byte[]? ProtectedPfx { get; private set; }
    public byte[]? ProtectedPassword { get; private set; }
    public string? CertificateSubject { get; private set; }
    public string? CertificateThumbprint { get; private set; }
    public DateTimeOffset? CertificateNotBeforeUtc { get; private set; }
    public DateTimeOffset? CertificateNotAfterUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string issuerCuit, int pointOfSale, string issuerName,
        string issuerAddress, string? issuerIibb, DateOnly? issuerActivityStartDate,
        Guid updatedByUserId, DateTimeOffset updatedAtUtc)
    {
        if (issuerCuit is null || issuerCuit.Length != 11 || !issuerCuit.All(char.IsAsciiDigit))
            throw new ArgumentException("A numeric 11-digit CUIT is required.", nameof(issuerCuit));
        if (pointOfSale is <= 0 or >= 99999)
            throw new ArgumentOutOfRangeException(nameof(pointOfSale));
        if (string.IsNullOrWhiteSpace(issuerName) || issuerName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(issuerAddress) || issuerAddress.Trim().Length > 300 ||
            issuerIibb?.Trim().Length > 40 || updatedByUserId == Guid.Empty)
            throw new ArgumentException("Invalid issuer details.");
        IssuerCuit = issuerCuit;
        PointOfSale = pointOfSale;
        IssuerName = issuerName.Trim();
        IssuerAddress = issuerAddress.Trim();
        IssuerIibb = string.IsNullOrWhiteSpace(issuerIibb) ? null : issuerIibb.Trim();
        IssuerActivityStartDate = issuerActivityStartDate;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void AssignCertificate(byte[] protectedPfx, byte[]? protectedPassword,
        string subject, string thumbprint, DateTimeOffset notBeforeUtc,
        DateTimeOffset notAfterUtc, Guid updatedByUserId, DateTimeOffset updatedAtUtc)
    {
        if (protectedPfx.Length == 0 || string.IsNullOrWhiteSpace(subject) || subject.Length > 500 ||
            thumbprint.Length != 40 || !thumbprint.All(char.IsAsciiHexDigit) ||
            notAfterUtc <= notBeforeUtc || updatedByUserId == Guid.Empty)
            throw new ArgumentException("Invalid certificate metadata.");
        ProtectedPfx = protectedPfx;
        ProtectedPassword = protectedPassword;
        CertificateSubject = subject;
        CertificateThumbprint = thumbprint.ToUpperInvariant();
        CertificateNotBeforeUtc = notBeforeUtc.ToUniversalTime();
        CertificateNotAfterUtc = notAfterUtc.ToUniversalTime();
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }
}
