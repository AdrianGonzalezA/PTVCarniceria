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
        endpoints.MapPost("/api/users", CreateCashierAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapGet("/api/users/{userId:guid}/assignments", ListAssignmentsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPut("/api/users/{userId:guid}/assignments", ReplaceCashierAssignmentsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPut("/api/users/{userId:guid}/password", ResetCashierPasswordAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPatch("/api/users/{userId:guid}", UpdateUserAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapDelete("/api/users/{userId:guid}/sessions", RevokeUserSessionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor operationalContext,
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

        var companyId = operationalContext.Context.CompanyId;
        var users = db.Users.AsNoTracking().Where(user => db.UserAssignments.Any(assignment =>
            assignment.UserId == user.Id && assignment.CompanyId == companyId));
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

    private static async Task<IResult> CreateCashierAsync(
        CreateCashierRequest? request,
        PlatformAccessDbContext db,
        IPasswordHasher passwordHasher,
        OperationalContextAccessor operationalContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidUsername(request.Username) || !IsValidEmail(request.Email) ||
            !ValidPassword(request.Password) || !ValidBranchIds(request.BranchIds))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = operationalContext.Context.CompanyId;
        if (!await AllBranchesActiveAsync(db, companyId, request.BranchIds, cancellationToken))
            return Error(StatusCodes.Status400BadRequest, "BRANCH_NOT_AVAILABLE");
        var roleId = await db.Roles.AsNoTracking().Where(role => role.Code == "cashier")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (roleId is null) return Error(StatusCodes.Status503ServiceUnavailable, "CASHIER_ROLE_UNAVAILABLE");

        var user = UserIdentity.Create(request.Username, request.Email, passwordHasher.Hash(request.Password));
        db.Users.Add(user);
        db.UserAssignments.AddRange(request.BranchIds.Select(branchId =>
            new UserAssignment(user.Id, roleId.Value, companyId, branchId)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "USER_ALREADY_EXISTS");
        }
        return Results.Created($"/api/users/{user.Id}", new UserListItemResponse(
            user.Id, user.Username, user.Email, user.IsActive, user.CreatedAtUtc));
    }

    private static async Task<IResult> ListAssignmentsAsync(
        Guid userId,
        PlatformAccessDbContext db,
        OperationalContextAccessor operationalContext,
        CancellationToken cancellationToken)
    {
        var companyId = operationalContext.Context.CompanyId;
        if (!await IsAssignedToCompanyAsync(db, userId, companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        var assignments = await db.UserAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.CompanyId == companyId)
            .Join(db.Roles.AsNoTracking(), assignment => assignment.RoleId, role => role.Id,
                (assignment, role) => new { assignment.BranchId, role.Code })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new UserAssignmentsResponse(
            assignments.Any(assignment => assignment.Code == "administrator") ? "administrator" : "cashier",
            assignments.Where(assignment => assignment.Code == "cashier" && assignment.BranchId.HasValue)
                .Select(assignment => assignment.BranchId!.Value).Order().ToArray()));
    }

    private static async Task<IResult> ReplaceCashierAssignmentsAsync(
        Guid userId,
        ReplaceAssignmentsRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor operationalContext,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (userId == Guid.Empty || request is null || !ValidBranchIds(request.BranchIds))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (userId == operationalContext.Context.UserId)
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");

        var companyId = operationalContext.Context.CompanyId;
        if (!await IsAssignedToCompanyAsync(db, userId, companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        if (!await AllBranchesActiveAsync(db, companyId, request.BranchIds, cancellationToken))
            return Error(StatusCodes.Status400BadRequest, "BRANCH_NOT_AVAILABLE");

        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(item => item.Code == "cashier",
            cancellationToken);
        if (role is null) return Error(StatusCodes.Status503ServiceUnavailable, "CASHIER_ROLE_UNAVAILABLE");
        var assignments = await db.UserAssignments.Where(item => item.CompanyId == companyId &&
            item.UserId == userId).ToArrayAsync(cancellationToken);
        if (assignments.Any(assignment => assignment.RoleId != role.Id || !assignment.BranchId.HasValue))
            return Error(StatusCodes.Status409Conflict, "ADMIN_ASSIGNMENT_IMMUTABLE");

        var requested = request.BranchIds.ToHashSet();
        var removed = assignments.Where(assignment => !requested.Contains(assignment.BranchId!.Value)).ToArray();
        foreach (var assignment in removed)
            if (await HasOpenOperationsAsync(db, userId, companyId, assignment.BranchId, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "USER_HAS_OPEN_OPERATIONS");

        var current = assignments.Select(assignment => assignment.BranchId!.Value).ToHashSet();
        var added = requested.Except(current).ToArray();
        if (removed.Length > 0 || added.Length > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.UserAssignments.RemoveRange(removed);
            db.UserAssignments.AddRange(added.Select(branchId =>
                new UserAssignment(userId, role.Id, companyId, branchId)));
            var now = timeProvider.GetUtcNow();
            var sessions = await db.Sessions.Where(session => session.UserId == userId &&
                session.CompanyId == companyId && session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now).ToArrayAsync(cancellationToken);
            foreach (var session in sessions) session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return Results.Ok(new UserAssignmentsResponse("cashier", requested.Order().ToArray()));
    }

    private static async Task<IResult> ResetCashierPasswordAsync(
        Guid userId,
        ResetPasswordRequest? request,
        PlatformAccessDbContext db,
        IPasswordHasher passwordHasher,
        OperationalContextAccessor operationalContext,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (userId == Guid.Empty || request is null || !ValidPassword(request.Password))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (userId == operationalContext.Context.UserId)
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");

        var companyId = operationalContext.Context.CompanyId;
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId &&
            db.UserAssignments.Any(assignment => assignment.UserId == item.Id &&
                assignment.CompanyId == companyId), cancellationToken);
        if (user is null) return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        var cashierRoleId = await db.Roles.AsNoTracking().Where(role => role.Code == "cashier")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (cashierRoleId is null || await db.UserAssignments.AnyAsync(assignment =>
            assignment.UserId == userId && db.Roles.Any(role => role.Id == assignment.RoleId &&
                role.Code == "administrator"), cancellationToken))
            return Error(StatusCodes.Status409Conflict, "ADMIN_PASSWORD_RESET_REJECTED");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.SetPasswordHash(passwordHasher.Hash(request.Password));
        var now = timeProvider.GetUtcNow();
        var sessions = await db.Sessions.Where(session => session.UserId == userId &&
            session.RevokedAtUtc == null && session.ExpiresAtUtc > now)
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

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
        if (user is null || !await IsAssignedToCompanyAsync(db, userId,
                operationalContext.Context.CompanyId, cancellationToken))
        {
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        }

        if (request.IsActive == false && user.IsActive &&
            await HasOpenOperationsAsync(db, userId, null,
                null, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "USER_HAS_OPEN_OPERATIONS");

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

        if (!await IsAssignedToCompanyAsync(db, userId, operationalContext.Context.CompanyId,
                cancellationToken))
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

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var normalized = email.Trim();
        return normalized.Normalize(NormalizationForm.FormKC).Length <= 320 &&
               System.Net.Mail.MailAddress.TryCreate(normalized, out var address) &&
               string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidUsername(string? username) =>
        !string.IsNullOrWhiteSpace(username) &&
        username.Trim().Normalize(NormalizationForm.FormKC).Length <= 100;

    private static bool ValidPassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= 12 &&
        Encoding.UTF8.GetByteCount(password) <= 1024;

    private static bool ValidBranchIds(Guid[]? branchIds) =>
        branchIds is { Length: > 0 and <= 100 } &&
        branchIds.All(id => id != Guid.Empty) && branchIds.Distinct().Count() == branchIds.Length;

    private static Task<bool> IsAssignedToCompanyAsync(
        PlatformAccessDbContext db, Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        db.UserAssignments.AnyAsync(assignment => assignment.UserId == userId &&
            assignment.CompanyId == companyId, cancellationToken);

    private static async Task<bool> AllBranchesActiveAsync(
        PlatformAccessDbContext db, Guid companyId, Guid[] branchIds,
        CancellationToken cancellationToken) =>
        await db.Branches.CountAsync(branch => branch.CompanyId == companyId && branch.IsActive &&
            branchIds.Contains(branch.Id), cancellationToken) == branchIds.Length;

    private static async Task<bool> HasOpenOperationsAsync(
        PlatformAccessDbContext db, Guid userId, Guid? companyId, Guid? branchId,
        CancellationToken cancellationToken) =>
        await db.CashierShifts.AnyAsync(shift => (!companyId.HasValue || shift.CompanyId == companyId.Value) &&
            shift.CashierId == userId && (!branchId.HasValue || shift.BranchId == branchId.Value) &&
            shift.Status == CashierShiftStatus.Open, cancellationToken) ||
        await db.SaleDrafts.AnyAsync(draft => (!companyId.HasValue || draft.CompanyId == companyId.Value) &&
            draft.UserId == userId && (!branchId.HasValue || draft.BranchId == branchId.Value) &&
            draft.Status == SaleDraftStatus.Draft, cancellationToken);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record UpdateUserRequest(string? Username, string? Email, bool? IsActive);
    private sealed record CreateCashierRequest(string Username, string Email, string Password, Guid[] BranchIds);
    private sealed record ReplaceAssignmentsRequest(Guid[] BranchIds);
    private sealed record ResetPasswordRequest(string Password);
    private sealed record UserAssignmentsResponse(string Role, Guid[] BranchIds);
}
