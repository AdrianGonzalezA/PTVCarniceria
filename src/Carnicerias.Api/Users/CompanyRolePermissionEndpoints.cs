using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Users;

public static class CompanyRolePermissionEndpoints
{
    private static readonly string[] ConfigurableRoles = ["administrator", "cashier"];
    private static readonly string[] ConfigurablePermissions =
        [PlatformPermissionCatalog.InventoryReceiptsManage];

    public static IEndpointRouteBuilder MapCompanyRolePermissionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/role-permissions", ListAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.RolesManage);
        endpoints.MapPut("/api/users/role-permissions/{roleCode}/{permissionCode}", SetAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.RolesManage);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking()
            .Where(role => ConfigurableRoles.Contains(role.Code))
            .Select(role => new { role.Id, role.Code, role.Name })
            .ToArrayAsync(cancellationToken);
        var permissions = await db.Permissions.AsNoTracking()
            .Where(permission => ConfigurablePermissions.Contains(permission.Code))
            .Select(permission => new { permission.Id, permission.Code, permission.Description })
            .ToArrayAsync(cancellationToken);
        var roleIds = roles.Select(role => role.Id).ToArray();
        var permissionIds = permissions.Select(permission => permission.Id).ToArray();
        var defaults = await db.RolePermissions.AsNoTracking().Where(link =>
                roleIds.Contains(link.RoleId) && permissionIds.Contains(link.PermissionId))
            .Select(link => new { link.RoleId, link.PermissionId })
            .ToArrayAsync(cancellationToken);
        var overrides = await db.CompanyRolePermissionOverrides.AsNoTracking()
            .Where(item => item.CompanyId == accessor.Context.CompanyId &&
                roleIds.Contains(item.RoleId) && permissionIds.Contains(item.PermissionId))
            .Select(item => new { item.RoleId, item.PermissionId, item.IsGranted })
            .ToArrayAsync(cancellationToken);
        var items = (from permission in permissions
                     from role in roles
                     orderby permission.Code, role.Code
                     let custom = overrides.SingleOrDefault(item =>
                         item.RoleId == role.Id && item.PermissionId == permission.Id)
                     select new RolePermissionResponse(role.Code, role.Name,
                         permission.Code, permission.Description,
                         custom?.IsGranted ?? defaults.Any(item =>
                             item.RoleId == role.Id && item.PermissionId == permission.Id)))
            .ToArray();
        return Results.Ok(items);
    }

    private static async Task<IResult> SetAsync(string roleCode, string permissionCode,
        SetRolePermissionRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, HttpContext http, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || !ConfigurableRoles.Contains(roleCode, StringComparer.Ordinal) ||
            !ConfigurablePermissions.Contains(permissionCode, StringComparer.Ordinal))
            return Error(400, "VALIDATION_ERROR");
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Code == roleCode, cancellationToken);
        var permission = await db.Permissions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Code == permissionCode, cancellationToken);
        if (role is null || permission is null) return Error(503, "ROLE_PERMISSION_UNAVAILABLE");
        var companyId = accessor.Context.CompanyId;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            var entry = await db.CompanyRolePermissionOverrides.SingleOrDefaultAsync(item =>
                item.CompanyId == companyId && item.RoleId == role.Id &&
                item.PermissionId == permission.Id, cancellationToken);
            if (entry is null)
            {
                entry = new CompanyRolePermissionOverride(companyId, role.Id,
                    permission.Id, request.IsGranted, accessor.Context.UserId, clock.GetUtcNow());
                db.CompanyRolePermissionOverrides.Add(entry);
            }
            else if (entry.IsGranted != request.IsGranted)
                entry.Set(request.IsGranted, accessor.Context.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new RolePermissionResponse(role.Code, role.Name,
                permission.Code, permission.Description, entry.IsGranted));
        }
        catch (Exception exception) when (exception is DbUpdateException
            { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } }
            or PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected })
        {
            return Error(409, "ROLE_PERMISSION_CHANGED");
        }
    }

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record SetRolePermissionRequest(bool IsGranted);
    private sealed record RolePermissionResponse(string RoleCode, string RoleName,
        string PermissionCode, string PermissionDescription, bool IsGranted);
}
