namespace Carnicerias.Infrastructure;

public enum TaxKind
{
    Vat,
    Other
}

public sealed class TaxCatalogEntry
{
    private TaxCatalogEntry()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public TaxCatalogEntry(Guid companyId, string code, string name, TaxKind kind,
        decimal ratePercent, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Company and actor are required.");
        var normalizedCode = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 40 ||
            normalizedCode.Any(character => !char.IsAsciiLetterUpper(character) &&
                !char.IsAsciiDigit(character) && character is not '_' and not '-'))
            throw new ArgumentException("Code must be 1-40 ASCII letters, digits, '-' or '_'.", nameof(code));
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 120)
            throw new ArgumentException("Name must be 1-120 characters.", nameof(name));
        if (!Enum.IsDefined(kind) || ratePercent is < 0 or > 100 ||
            decimal.Round(ratePercent, 2) != ratePercent)
            throw new ArgumentOutOfRangeException(nameof(ratePercent));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        Code = normalizedCode;
        Name = normalizedName;
        Kind = kind;
        RatePercent = ratePercent;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public TaxKind Kind { get; private set; }
    public decimal RatePercent { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public void Deactivate(Guid actorId, DateTimeOffset atUtc)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (!IsActive) return;
        var utc = atUtc.ToUniversalTime();
        if (utc < CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(atUtc));
        IsActive = false;
        DeactivatedByUserId = actorId;
        DeactivatedAtUtc = utc;
    }
}
