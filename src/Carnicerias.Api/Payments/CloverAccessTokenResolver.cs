using System.Security.Cryptography;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public sealed record CloverResolvedAccess(string? Token, string? MerchantId,
    DateTimeOffset? UpdatedAtUtc, string? ErrorCode);

/// <summary>
/// Reserves a single-use Clover refresh before calling Clover. An ambiguous result is never retried:
/// a new OAuth authorization is required to clear the durable reservation.
/// </summary>
public sealed partial class CloverAccessTokenResolver(PlatformAccessDbContext db,
    CloverSettingsProtector protector, CloverOAuthClient oauth, TimeProvider clock,
    ILogger<CloverAccessTokenResolver> logger)
{
    public async Task<CloverResolvedAccess> ResolveAsync(Guid companyId, Guid actorId,
        CancellationToken cancellationToken)
    {
        Guid? reservation = null;
        string? clientId = null;
        string? refreshToken = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var settings = await LockSettingsAsync(companyId, cancellationToken);
            var now = clock.GetUtcNow();
            if (settings is not { IsConnected: true }) return Failure("CLOVER_NOT_CONNECTED");
            if (settings.RefreshAttemptId is not null) return Failure("CLOVER_REFRESH_UNCERTAIN");
            if (settings.AccessTokenExpiresAtUtc > now.AddMinutes(2))
            {
                try
                {
                    return new CloverResolvedAccess(protector.Unprotect(settings.ProtectedAccessToken!),
                        settings.MerchantId, settings.UpdatedAtUtc, null);
                }
                catch (CryptographicException) { return Failure("CLOVER_CREDENTIAL_UNAVAILABLE"); }
            }
            if (settings.RefreshTokenExpiresAtUtc <= now)
                return Failure("CLOVER_REFRESH_EXPIRED");
            try { refreshToken = protector.Unprotect(settings.ProtectedRefreshToken!); }
            catch (CryptographicException) { return Failure("CLOVER_CREDENTIAL_UNAVAILABLE"); }
            clientId = settings.ClientId;
            reservation = Guid.NewGuid();
            settings.BeginRefresh(reservation.Value, actorId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // From this point request cancellation must not interrupt persistence of a consumed token.
        // HttpClient has its own 20-second timeout; the reservation survives any ambiguous outcome.
        db.ChangeTracker.Clear();
        CloverOAuthTokenResponse? tokens;
        try { tokens = await oauth.RefreshAsync(clientId!, refreshToken!, CancellationToken.None); }
        catch (Exception exception)
        {
            LogRefreshResponseUncertain(logger, companyId, exception);
            return Failure("CLOVER_REFRESH_UNCERTAIN");
        }
        if (tokens is null)
        {
            LogRefreshResponseUncertain(logger, companyId, null);
            return Failure("CLOVER_REFRESH_UNCERTAIN");
        }

        try
        {
            using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var transaction = await db.Database.BeginTransactionAsync(saveTimeout.Token);
            var settings = await LockSettingsAsync(companyId, saveTimeout.Token);
            if (settings?.RefreshAttemptId != reservation)
                return Failure("CLOVER_CONFIGURATION_CHANGED");
            settings.ReplaceTokens(protector.Protect(tokens.AccessToken),
                protector.Protect(tokens.RefreshToken), tokens.AccessExpiresAtUtc,
                tokens.RefreshExpiresAtUtc, actorId, clock.GetUtcNow());
            await db.SaveChangesAsync(saveTimeout.Token);
            await transaction.CommitAsync(saveTimeout.Token);
            return new CloverResolvedAccess(tokens.AccessToken, settings.MerchantId,
                settings.UpdatedAtUtc, null);
        }
        catch (Exception exception)
        {
            LogRefreshPersistenceUncertain(logger, companyId, exception);
            return Failure("CLOVER_REFRESH_UNCERTAIN");
        }
    }

    private async Task<CloverGatewaySettings?> LockSettingsAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        var rows = await db.CloverGatewaySettings.FromSqlInterpolated(
            $"SELECT * FROM payments_cash.clover_gateway_settings WHERE \"CompanyId\" = {companyId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private static CloverResolvedAccess Failure(string code) => new(null, null, null, code);

    [LoggerMessage(EventId = 6801, Level = LogLevel.Warning,
        Message = "Clover refresh response uncertain for company {CompanyId}")]
    private static partial void LogRefreshResponseUncertain(ILogger logger, Guid companyId,
        Exception? exception);

    [LoggerMessage(EventId = 6802, Level = LogLevel.Warning,
        Message = "Clover refresh could not be persisted for company {CompanyId}")]
    private static partial void LogRefreshPersistenceUncertain(ILogger logger, Guid companyId,
        Exception exception);
}
