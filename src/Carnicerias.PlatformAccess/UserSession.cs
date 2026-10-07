namespace Carnicerias.PlatformAccess;

public sealed class UserSession
{
    private UserSession()
    {
        TokenHash = string.Empty;
    }

    public UserSession(Guid userId, string tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        UserId = userId == Guid.Empty
            ? throw new ArgumentException("User id is required.", nameof(userId))
            : userId;
        TokenHash = string.IsNullOrWhiteSpace(tokenHash) ||
                    tokenHash.Length != 64 ||
                    !tokenHash.All(char.IsAsciiHexDigit)
            ? throw new ArgumentException("A SHA-256 token hash is required.", nameof(tokenHash))
            : tokenHash;
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Session expiration must follow its creation time.", nameof(expiresAtUtc));
        }

        Id = Guid.NewGuid();
        CreatedAtUtc = createdAtUtc;
        LastActivityAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastActivityAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? CompanyId { get; private set; }

    public Guid? BranchId { get; private set; }

    public Guid? PosTerminalId { get; private set; }

    public void BindToTerminal(Guid terminalId)
    {
        if (terminalId == Guid.Empty)
            throw new ArgumentException("Terminal id is required.", nameof(terminalId));
        if (PosTerminalId is not null && PosTerminalId != terminalId)
            throw new InvalidOperationException("A session cannot move to another terminal.");

        PosTerminalId = terminalId;
    }

    public void SelectOperationalContext(Guid companyId, Guid branchId)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
        {
            throw new ArgumentException("A valid company and branch are required.");
        }

        CompanyId = companyId;
        BranchId = branchId;
    }

    public void ClearOperationalContext()
    {
        CompanyId = null;
        BranchId = null;
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    public void Touch(DateTimeOffset now)
    {
        if (!IsActiveAt(now))
        {
            throw new InvalidOperationException("Only active sessions can be updated.");
        }

        if (now > LastActivityAtUtc)
        {
            LastActivityAtUtc = now;
        }
    }

    public void Revoke(DateTimeOffset now)
    {
        if (IsActiveAt(now))
        {
            RevokedAtUtc = now;
        }
    }
}
