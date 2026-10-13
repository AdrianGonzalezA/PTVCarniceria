namespace Carnicerias.Infrastructure;

/// <summary>Company-scoped Clover sandbox OAuth configuration. Secret bytes are protected by the API.</summary>
public sealed class CloverGatewaySettings
{
    private CloverGatewaySettings() { }

    public CloverGatewaySettings(Guid companyId, string clientId, string merchantId,
        Guid actorId, DateTimeOffset now)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.");
        CompanyId = companyId;
        UpdateIdentity(clientId, merchantId, actorId, now);
    }

    public Guid CompanyId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string MerchantId { get; private set; } = string.Empty;
    public byte[]? ProtectedClientSecret { get; private set; }
    public byte[]? ProtectedAccessToken { get; private set; }
    public byte[]? ProtectedRefreshToken { get; private set; }
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; private set; }
    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; private set; }
    /// <summary>A durable, single-use refresh reservation. Never retry while this is set.</summary>
    public Guid? RefreshAttemptId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool HasClientSecret => ProtectedClientSecret is { Length: > 0 };
    public bool IsConnected => ProtectedAccessToken is { Length: > 0 } &&
        ProtectedRefreshToken is { Length: > 0 } &&
        AccessTokenExpiresAtUtc is not null && RefreshTokenExpiresAtUtc is not null;

    public void UpdateIdentity(string clientId, string merchantId, Guid actorId, DateTimeOffset now)
    {
        if (!ValidIdentifier(clientId) || !ValidIdentifier(merchantId))
            throw new ArgumentException("Invalid Clover app or merchant identifier.");
        if (!string.Equals(ClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(MerchantId, merchantId, StringComparison.Ordinal))
        {
            ProtectedClientSecret = null;
            ClearTokens();
        }
        ClientId = clientId;
        MerchantId = merchantId;
        Touch(actorId, now);
    }

    public void ReplaceClientSecret(byte[] protectedSecret, Guid actorId, DateTimeOffset now)
    {
        if (protectedSecret.Length == 0) throw new ArgumentException("Secret cannot be empty.");
        ProtectedClientSecret = protectedSecret.ToArray();
        ClearTokens();
        Touch(actorId, now);
    }

    public void ReplaceTokens(byte[] protectedAccessToken, byte[] protectedRefreshToken,
        DateTimeOffset accessExpiresAtUtc, DateTimeOffset refreshExpiresAtUtc,
        Guid actorId, DateTimeOffset now)
    {
        if (protectedAccessToken.Length == 0 || protectedRefreshToken.Length == 0 ||
            accessExpiresAtUtc <= now || refreshExpiresAtUtc <= accessExpiresAtUtc)
            throw new ArgumentException("Invalid Clover OAuth token pair.");
        ProtectedAccessToken = protectedAccessToken.ToArray();
        ProtectedRefreshToken = protectedRefreshToken.ToArray();
        AccessTokenExpiresAtUtc = accessExpiresAtUtc.ToUniversalTime();
        RefreshTokenExpiresAtUtc = refreshExpiresAtUtc.ToUniversalTime();
        RefreshAttemptId = null;
        Touch(actorId, now);
    }

    public void BeginRefresh(Guid attemptId, Guid actorId, DateTimeOffset now)
    {
        if (attemptId == Guid.Empty || !IsConnected || RefreshAttemptId is not null)
            throw new InvalidOperationException("Clover refresh is unavailable or already reserved.");
        RefreshAttemptId = attemptId;
        Touch(actorId, now);
    }

    private static bool ValidIdentifier(string? value) => value is { Length: > 0 and <= 100 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private void ClearTokens()
    {
        ProtectedAccessToken = null;
        ProtectedRefreshToken = null;
        AccessTokenExpiresAtUtc = null;
        RefreshTokenExpiresAtUtc = null;
        RefreshAttemptId = null;
    }

    private void Touch(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.");
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now.ToUniversalTime();
    }
}

public sealed class CloverOAuthAttempt
{
    private CloverOAuthAttempt() { }

    public CloverOAuthAttempt(string stateHash, Guid companyId, Guid userId,
        DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (stateHash.Length != 64 || !stateHash.All(Uri.IsHexDigit) ||
            companyId == Guid.Empty || userId == Guid.Empty || expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Invalid OAuth attempt.");
        StateHash = stateHash;
        CompanyId = companyId;
        UserId = userId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    public string StateHash { get; private set; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
}
