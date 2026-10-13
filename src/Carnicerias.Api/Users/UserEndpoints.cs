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
        endpoints.MapGet("/api/users/password-policy", (PlatformAccessDbContext db) =>
                Results.Ok(new PasswordPolicyResponse(
                    VisualDevelopmentPasswordPolicy.MinimumLength(db.Database.GetConnectionString()))))
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPost("/api/users", CreateUserAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapGet("/api/users/{userId:guid}/assignments", ListAssignmentsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPut("/api/users/{userId:guid}/assignments", ReplaceAssignmentsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.UsersManage);
        endpoints.MapPut("/api/users/{userId:guid}/password", ResetUserPasswordAsync)
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
        var users = db.Users.AsNoTracking().Where(user => user.CompanyId == companyId && !user.IsManufacturer);
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

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest? request,
        PlatformAccessDbContext db,
        IPasswordHasher passwordHasher,
        OperationalContextAccessor operationalContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        var roleCode = request?.Role ?? "cashier";
        var branchIds = request?.BranchIds ?? [];
        if (request is null || !ValidUsername(request.Username) || !IsValidEmail(request.Email) ||
            !VisualDevelopmentPasswordPolicy.IsValid(request.Password, db.Database.GetConnectionString()) ||
            roleCode is not ("cashier" or "administrator") ||
            (roleCode == "cashier" && !ValidBranchIds(branchIds)) ||
            (roleCode == "administrator" && branchIds.Length != 0))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = operationalContext.Context.CompanyId;
        if (roleCode == "cashier" && !await AllBranchesActiveAsync(db, companyId, branchIds, cancellationToken))
            return Error(StatusCodes.Status400BadRequest, "BRANCH_NOT_AVAILABLE");
        var roleId = await db.Roles.AsNoTracking().Where(role => role.Code == roleCode)
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (roleId is null) return Error(StatusCodes.Status503ServiceUnavailable, "ROLE_UNAVAILABLE");

        var user = UserIdentity.CreateForCompany(companyId, request.Username, request.Email,
            passwordHasher.Hash(request.Password));
        db.Users.Add(user);
        db.UserAssignments.AddRange(roleCode == "administrator"
            ? [new UserAssignment(user.Id, roleId.Value, companyId)]
            : branchIds.Select(branchId => new UserAssignment(user.Id, roleId.Value, companyId, branchId)));
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

    private static async Task<IResult> ReplaceAssignmentsAsync(
        Guid userId,
        ReplaceAssignmentsRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor operationalContext,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        var roleCode = request?.Role ?? "cashier";
        var branchIds = request?.BranchIds ?? [];
        if (userId == Guid.Empty || request is null || roleCode is not ("cashier" or "administrator") ||
            (roleCode == "cashier" && !ValidBranchIds(branchIds)) ||
            (roleCode == "administrator" && branchIds.Length != 0))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (userId == operationalContext.Context.UserId)
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");

        var companyId = operationalContext.Context.CompanyId;
        if (!await IsAssignedToCompanyAsync(db, userId, companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        if (roleCode == "cashier" && !await AllBranchesActiveAsync(db, companyId, branchIds, cancellationToken))
            return Error(StatusCodes.Status400BadRequest, "BRANCH_NOT_AVAILABLE");

        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(item => item.Code == roleCode,
            cancellationToken);
        if (role is null) return Error(StatusCodes.Status503ServiceUnavailable, "ROLE_UNAVAILABLE");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockCompanyAsync(db, companyId, cancellationToken);
        var assignments = await db.UserAssignments.Where(item => item.CompanyId == companyId &&
            item.UserId == userId).ToArrayAsync(cancellationToken);
        var adminRoleId = await db.Roles.AsNoTracking().Where(item => item.Code == "administrator")
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
        if (adminRoleId is null) return Error(StatusCodes.Status503ServiceUnavailable, "ROLE_UNAVAILABLE");
        var wasAdministrator = assignments.Any(assignment => assignment.RoleId == adminRoleId);
        if (wasAdministrator && roleCode != "administrator" &&
            !await HasAnotherActiveAdministratorAsync(db, companyId, userId, adminRoleId.Value, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR_REQUIRED");

        var requested = branchIds.ToHashSet();
        var unchanged = assignments.Length == (roleCode == "administrator" ? 1 : requested.Count) &&
            assignments.All(assignment => assignment.RoleId == role.Id &&
                (roleCode == "administrator" ? assignment.BranchId is null :
                    assignment.BranchId is Guid branchId && requested.Contains(branchId)));
        if (!unchanged)
        {
            foreach (var assignment in assignments)
                if (assignment.BranchId is Guid existingBranchId &&
                    (roleCode != "cashier" || !requested.Contains(existingBranchId)) &&
                    await HasOpenOperationsAsync(db, userId, companyId, existingBranchId, cancellationToken))
                    return Error(StatusCodes.Status409Conflict, "USER_HAS_OPEN_OPERATIONS");
            if (wasAdministrator && roleCode != "administrator" &&
                await HasOpenOperationsAsync(db, userId, companyId, null, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "USER_HAS_OPEN_OPERATIONS");

            db.UserAssignments.RemoveRange(assignments);
            await db.SaveChangesAsync(cancellationToken);
            db.UserAssignments.AddRange(roleCode == "administrator"
                ? [new UserAssignment(userId, role.Id, companyId)]
                : branchIds.Select(branchId => new UserAssignment(userId, role.Id, companyId, branchId)));
            var now = timeProvider.GetUtcNow();
            var sessions = await db.Sessions.Where(session => session.UserId == userId &&
                session.CompanyId == companyId && session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now).ToArrayAsync(cancellationToken);
            foreach (var session in sessions) session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new UserAssignmentsResponse(roleCode,
            roleCode == "cashier" ? requested.Order().ToArray() : []));
    }

    private static async Task<IResult> ResetUserPasswordAsync(
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
        if (userId == Guid.Empty || request is null ||
            !VisualDevelopmentPasswordPolicy.IsValid(request.Password, db.Database.GetConnectionString()))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = operationalContext.Context.CompanyId;
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId &&
            item.CompanyId == companyId && !item.IsManufacturer, cancellationToken);
        if (user is null) return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");

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
            (request.Username is not null && !ValidUsername(request.Username)) ||
            (request.Email is not null && !IsValidEmail(request.Email)))
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }

        if (request.IsActive == false && userId == operationalContext.Context.UserId)
        {
            return Error(StatusCodes.Status409Conflict, "SELF_ACCESS_CHANGE_REJECTED");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var companyId = operationalContext.Context.CompanyId;
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId &&
            candidate.CompanyId == companyId && !candidate.IsManufacturer, cancellationToken);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND");
        }

        if (request.IsActive == false && user.IsActive)
        {
            await LockCompanyAsync(db, companyId, cancellationToken);
            var administratorRoleId = await db.Roles.AsNoTracking().Where(role => role.Code == "administrator")
                .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
            if (administratorRoleId is Guid roleId &&
                await db.UserAssignments.AnyAsync(assignment => assignment.UserId == userId &&
                    assignment.CompanyId == companyId && assignment.RoleId == roleId, cancellationToken) &&
                !await HasAnotherActiveAdministratorAsync(db, companyId, userId, roleId, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR_REQUIRED");
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
        username.Trim().Normalize(NormalizationForm.FormKC).Length <= 100 &&
        !username.Trim().Normalize(NormalizationForm.FormKC).Contains('@');

    private static bool ValidBranchIds(Guid[]? branchIds) =>
        branchIds is { Length: > 0 and <= 100 } &&
        branchIds.All(id => id != Guid.Empty) && branchIds.Distinct().Count() == branchIds.Length;

    private static Task<bool> IsAssignedToCompanyAsync(
        PlatformAccessDbContext db, Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id == userId && user.CompanyId == companyId &&
            !user.IsManufacturer && db.UserAssignments.Any(assignment => assignment.UserId == user.Id &&
                assignment.CompanyId == companyId), cancellationToken);

    private static Task<bool> HasAnotherActiveAdministratorAsync(
        PlatformAccessDbContext db, Guid companyId, Guid excludedUserId, Guid roleId,
        CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id != excludedUserId && user.CompanyId == companyId &&
            user.IsActive && !user.IsManufacturer && db.UserAssignments.Any(assignment =>
                assignment.UserId == user.Id && assignment.CompanyId == companyId &&
                assignment.RoleId == roleId && assignment.BranchId == null), cancellationToken);

    private static Task<int> LockCompanyAsync(
        PlatformAccessDbContext db, Guid companyId, CancellationToken cancellationToken)
    {
        var bytes = companyId.ToByteArray();
        var first = BitConverter.ToInt32(bytes, 0);
        var second = BitConverter.ToInt32(bytes, 4);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({first}, {second})", cancellationToken);
    }

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
    private sealed record CreateUserRequest(string Username, string Email, string Password, Guid[]? BranchIds,
        string? Role);
    private sealed record ReplaceAssignmentsRequest(Guid[]? BranchIds, string? Role);
    private sealed record ResetPasswordRequest(string Password);
    private sealed record PasswordPolicyResponse(int MinimumLength);
    private sealed record UserAssignmentsResponse(string Role, Guid[] BranchIds);
}
