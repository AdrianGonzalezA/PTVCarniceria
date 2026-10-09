namespace Carnicerias.Infrastructure;

/// <summary>Company-scoped Mercado Pago test credentials. Plaintext is never persisted.</summary>
public sealed class MercadoPagoGatewaySettings
{
    private MercadoPagoGatewaySettings() { }

    public MercadoPagoGatewaySettings(Guid companyId, string sellerUserId, Guid actorId, DateTimeOffset now)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.");
        CompanyId = companyId;
        UpdateSeller(sellerUserId, actorId, now);
    }

    public Guid CompanyId { get; private set; }
    public string SellerUserId { get; private set; } = string.Empty;
    public byte[]? ProtectedAccessToken { get; private set; }
    public byte[]? ProtectedWebhookSecret { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool HasAccessToken => ProtectedAccessToken is { Length: > 0 };
    public bool HasWebhookSecret => ProtectedWebhookSecret is { Length: > 0 };

    public void UpdateSeller(string sellerUserId, Guid actorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sellerUserId) || sellerUserId.Length > 30 ||
            !sellerUserId.All(char.IsAsciiDigit) || actorId == Guid.Empty)
            throw new ArgumentException("A numeric seller test user id is required.");
        SellerUserId = sellerUserId;
        Touch(actorId, now);
    }

    public void ReplaceAccessToken(byte[] encrypted, Guid actorId, DateTimeOffset now)
    {
        if (encrypted.Length == 0) throw new ArgumentException("Token cannot be empty.");
        ProtectedAccessToken = encrypted.ToArray();
        Touch(actorId, now);
    }

    public void ReplaceWebhookSecret(byte[] encrypted, Guid actorId, DateTimeOffset now)
    {
        if (encrypted.Length == 0) throw new ArgumentException("Secret cannot be empty.");
        ProtectedWebhookSecret = encrypted.ToArray();
        Touch(actorId, now);
    }

    private void Touch(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.");
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now.ToUniversalTime();
    }
}

/// <summary>Maps an application register to either or both Mercado Pago in-person modes.</summary>
public sealed class MercadoPagoRegisterSettings
{
    private MercadoPagoRegisterSettings() { }

    public MercadoPagoRegisterSettings(Guid companyId, Guid branchId, Guid posTerminalId,
        string? qrExternalPosId, string? pointTerminalId, Guid actorId, DateTimeOffset now)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || posTerminalId == Guid.Empty)
            throw new ArgumentException("A company, branch and register are required.");
        CompanyId = companyId;
        BranchId = branchId;
        PosTerminalId = posTerminalId;
        Update(qrExternalPosId, pointTerminalId, actorId, now);
    }

    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid PosTerminalId { get; private set; }
    public string? QrExternalPosId { get; private set; }
    public string? PointTerminalId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string? qrExternalPosId, string? pointTerminalId, Guid actorId, DateTimeOffset now)
    {
        static bool Invalid(string? value, int max) => value is not null &&
            (value.Length is 0 || value.Length > max ||
             !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'));
        if (Invalid(qrExternalPosId, 40) || Invalid(pointTerminalId, 100) || actorId == Guid.Empty)
            throw new ArgumentException("Invalid Mercado Pago register identifiers.");
        QrExternalPosId = qrExternalPosId;
        PointTerminalId = pointTerminalId;
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now.ToUniversalTime();
    }
}
