using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Users;

public static class UserEndpoints
{
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 100;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users", ListUsersAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPatch("/api/users/{userId:guid}", UpdateUserAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapDelete("/api/users/{userId:guid}/sessions", RevokeUserSessionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        PlatformAccessDbContext db,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? search = null)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaximumPageSize ||
            (search?.Length ?? 0) > MaximumSearchLength)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        var users = db.Users.AsNoTracking();
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            users = users.Where(user =>
                EF.Functions.ILike(user.UsernameNormalized, pattern, "\\") ||
                EF.Functions.ILike(user.EmailNormalized, pattern, "\\"));
        }

        var totalItems = await users.LongCountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.UsernameNormalized)
            .ThenBy(user => user.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(user => new UserListItemResponse(
                user.Id,
                user.Username,
                user.Email,
                user.IsActive,
                user.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new UserListResponse(
            items,
            page,
            pageSize,
            totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task<IResult> UpdateUserAsync(
        Guid userId,
        UpdateUserRequest? request,
        PlatformAccessDbContext db,
        TimeProvider timeProvider,
        OperationalContextAccessor operationalContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
        {
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        }

        if (userId == Guid.Empty || request is null ||
            (request.Username is null && request.Email is null && request.IsActive is null) ||
            (request.Username is not null &&
             (string.IsNullOrWhiteSpace(request.Username) ||
              request.Username.Trim().Normalize(NormalizationForm.FormKC).Length > 100)) ||
            (request.Email is not null && !IsValidEmail(request.Email)))
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        if (request.IsActive == false && userId == operationalContext.Context.UserId)
        {
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        }

        user.UpdateDetails(request.Username?.Trim() ?? user.Username, request.Email?.Trim() ?? user.Email);
        if (request.IsActive is bool isActive)
        {
            if (isActive)
            {
                user.Activate();
            }
            else
            {
                user.Deactivate();
                var now = timeProvider.GetUtcNow();
                var activeSessions = await db.Sessions
                    .Where(session => session.UserId == userId &&
                                      session.RevokedAtUtc == null &&
                                      session.ExpiresAtUtc > now)
                    .ToListAsync(cancellationToken);
                foreach (var session in activeSessions)
                {
                    session.Revoke(now);
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "USER_ALREADY_EXISTS");
        }

        return Results.Ok(new UserListItemResponse(
            user.Id,
            user.Username,
            user.Email,
            user.IsActive,
            user.CreatedAtUtc));
    }

    private static async Task<IResult> RevokeUserSessionsAsync(
        Guid userId,
        PlatformAccessDbContext db,
        TimeProvider timeProvider,
        OperationalContextAccessor operationalContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
        {
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        }

        if (userId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        if (userId == operationalContext.Context.UserId)
        {
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");
        }

        if (!await db.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var activeSessions = await db.Sessions
            .Where(session => session.UserId == userId &&
                              session.RevokedAtUtc == null &&
                              session.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new SessionRevocationResponse(activeSessions.Count));
    }

    private static bool IsValidEmail(string email)
    {
        var normalized = email.Trim();
        return normalized.Normalize(NormalizationForm.FormKC).Length <= 320 &&
               System.Net.Mail.MailAddress.TryCreate(normalized, out var address) &&
               string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record UpdateUserRequest(string? Username, string? Email, bool? IsActive);
}
