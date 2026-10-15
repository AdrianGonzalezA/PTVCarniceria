using System.Security.Cryptography;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Federation;

public static class AdminBranchCentralConnectionEndpoints
{
    public static IEndpointRouteBuilder MapAdminBranchCentralConnectionEndpoints(
        this IEndpointRouteBuilder endpoints, BranchServiceSettings binding)
    {
        var group = endpoints.MapGroup("/api/admin/central-connection")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage)
            .AddEndpointFilter(async (invocation, next) =>
            {
                invocation.HttpContext.Response.Headers.CacheControl = "no-store";
                invocation.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
                return await next(invocation);
            });
        group.MapGet("", (PlatformAccessDbContext db, OperationalContextAccessor accessor,
            TimeProvider clock, CancellationToken cancellationToken) =>
            GetAsync(db, accessor, binding, clock, cancellationToken));
        group.MapPut("", (UrlRequest? request, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, IConfiguration configuration, TimeProvider clock,
            HttpContext http, CancellationToken cancellationToken) =>
            SaveUrlAsync(request, db, accessor, binding, configuration, clock, http, cancellationToken));
        group.MapPost("/credentials", (PlatformAccessDbContext db,
            OperationalContextAccessor accessor, TimeProvider clock, HttpContext http,
            CancellationToken cancellationToken) =>
            IssueCredentialAsync(db, accessor, binding, clock, http, cancellationToken))
            .RequireRateLimiting("branch-central-credential");
        group.MapPost("/credentials/complete-rotation", (PlatformAccessDbContext db,
            OperationalContextAccessor accessor, TimeProvider clock, HttpContext http,
            CancellationToken cancellationToken) =>
            CompleteRotationAsync(db, accessor, binding, clock, http, cancellationToken));
        group.MapPost("/deactivation", (PlatformAccessDbContext db,
            OperationalContextAccessor accessor, TimeProvider clock, HttpContext http,
            CancellationToken cancellationToken) =>
            DeactivateAsync(db, accessor, binding, clock, http, cancellationToken));
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, BranchServiceSettings binding, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var scope = await ReadScopeAsync(db, accessor, binding, cancellationToken);
        if (scope is null) return Error(409, "BRANCH_BINDING_UNAVAILABLE");
        var connection = await db.BranchCentralConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == binding.CompanyId &&
                item.BranchId == binding.BranchId, cancellationToken);
        return Results.Ok(new ConnectionResponse(scope.CompanyId, scope.CompanyName,
            scope.BranchId, scope.BranchName, connection?.InstallationId,
            connection?.DestinationUrl, connection?.IsActive ?? false,
            connection?.CredentialSha256 is not null,
            connection?.PreviousCredentialSha256 is not null &&
                connection.PreviousCredentialExpiresAtUtc > clock.GetUtcNow(),
            connection?.PreviousCredentialExpiresAtUtc, connection?.Revision ?? 0));
    }

    private static async Task<IResult> SaveUrlAsync(UrlRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        BranchServiceSettings binding, IConfiguration configuration, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedMutation(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Revision < 0) return Error(400, "VALIDATION_ERROR");
        var scope = await ReadScopeAsync(db, accessor, binding, cancellationToken);
        if (scope is null) return Error(409, "BRANCH_BINDING_UNAVAILABLE");
        var connection = await db.BranchCentralConnections.SingleOrDefaultAsync(item =>
            item.CompanyId == scope.CompanyId && item.BranchId == scope.BranchId,
            cancellationToken);
        if (connection?.Revision != request.Revision && !(connection is null && request.Revision == 0))
            return Error(409, "CONNECTION_CHANGED");
        try
        {
            var allowLabLoopback = configuration.GetValue<bool>("Federation:AllowLabLoopback");
            var now = clock.GetUtcNow();
            if (connection is null)
            {
                connection = BranchCentralConnection.Create(scope.CompanyId, scope.BranchId,
                    request.DestinationUrl, allowLabLoopback, accessor.Context.UserId,
                    now);
                db.BranchCentralConnections.Add(connection);
            }
            else connection.UpdateUrl(request.DestinationUrl, allowLabLoopback,
                accessor.Context.UserId, now);
            db.BranchCentralConnectionEvents.Add(BranchCentralConnectionEvent.Create(
                scope.CompanyId, scope.BranchId, accessor.Context.UserId,
                BranchCentralConnectionAction.UrlSaved, now));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }
        catch (DbUpdateConcurrencyException) { return Error(409, "CONNECTION_CHANGED"); }
        catch (DbUpdateException) { return Error(409, "CONNECTION_CHANGED"); }
        return Results.Ok(new { connection.InstallationId, connection.DestinationUrl,
            connection.IsActive, connection.Revision });
    }

    private static async Task<IResult> IssueCredentialAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, BranchServiceSettings binding, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedMutation(http)) return Error(403, "CSRF_REJECTED");
        var scope = await ReadScopeAsync(db, accessor, binding, cancellationToken);
        if (scope is null) return Error(409, "BRANCH_BINDING_UNAVAILABLE");
        var connection = await db.BranchCentralConnections.SingleOrDefaultAsync(item =>
            item.CompanyId == scope.CompanyId && item.BranchId == scope.BranchId,
            cancellationToken);
        if (connection is null) return Error(409, "DESTINATION_NOT_CONFIGURED");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            var now = clock.GetUtcNow();
            var action = connection.IsActive
                ? BranchCentralConnectionAction.CredentialRotated
                : BranchCentralConnectionAction.CredentialIssued;
            connection.IssueCredential(token, now, accessor.Context.UserId);
            db.BranchCentralConnectionEvents.Add(BranchCentralConnectionEvent.Create(
                scope.CompanyId, scope.BranchId, accessor.Context.UserId, action, now));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException) { return Error(409, "ROTATION_IN_PROGRESS"); }
        catch (DbUpdateConcurrencyException) { return Error(409, "CONNECTION_CHANGED"); }
        return Results.Ok(new ConnectionBundle(1, scope.BranchName, connection.DestinationUrl,
            connection.InstallationId, scope.CompanyId, scope.BranchId, token));
    }

    private static async Task<IResult> CompleteRotationAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, BranchServiceSettings binding, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedMutation(http)) return Error(403, "CSRF_REJECTED");
        var connection = await ReadConnectionAsync(db, accessor, binding, cancellationToken);
        if (connection is null) return Error(404, "CONNECTION_NOT_FOUND");
        var completedAt = clock.GetUtcNow();
        connection.CompleteRotation(completedAt, accessor.Context.UserId);
        db.BranchCentralConnectionEvents.Add(BranchCentralConnectionEvent.Create(
            binding.CompanyId, binding.BranchId, accessor.Context.UserId,
            BranchCentralConnectionAction.RotationCompleted, completedAt));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Error(409, "CONNECTION_CHANGED"); }
        return Results.Ok(new { connection.IsActive, connection.Revision });
    }

    private static async Task<IResult> DeactivateAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, BranchServiceSettings binding, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedMutation(http)) return Error(403, "CSRF_REJECTED");
        var connection = await ReadConnectionAsync(db, accessor, binding, cancellationToken);
        if (connection is null) return Error(404, "CONNECTION_NOT_FOUND");
        var deactivatedAt = clock.GetUtcNow();
        connection.Deactivate(deactivatedAt, accessor.Context.UserId);
        db.BranchCentralConnectionEvents.Add(BranchCentralConnectionEvent.Create(
            binding.CompanyId, binding.BranchId, accessor.Context.UserId,
            BranchCentralConnectionAction.Deactivated, deactivatedAt));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Error(409, "CONNECTION_CHANGED"); }
        return Results.Ok(new { connection.IsActive, connection.Revision });
    }

    private static async Task<BranchCentralConnection?> ReadConnectionAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        BranchServiceSettings binding, CancellationToken cancellationToken)
    {
        if (await ReadScopeAsync(db, accessor, binding, cancellationToken) is null) return null;
        return await db.BranchCentralConnections.SingleOrDefaultAsync(item =>
            item.CompanyId == binding.CompanyId && item.BranchId == binding.BranchId,
            cancellationToken);
    }

    private static async Task<Scope?> ReadScopeAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, BranchServiceSettings binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsEnabled || !binding.IsManaged ||
            accessor.Context.CompanyId != binding.CompanyId ||
            accessor.Context.BranchId != binding.BranchId) return null;
        return await db.Branches.AsNoTracking()
            .Where(branch => branch.CompanyId == binding.CompanyId &&
                branch.Id == binding.BranchId && branch.IsActive)
            .Join(db.Companies.AsNoTracking().Where(company => company.IsActive),
                branch => branch.CompanyId, company => company.Id,
                (branch, company) => new Scope(company.Id, company.Name,
                    branch.Id, branch.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool AllowedMutation(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        return http.Request.Headers["X-Carnicerias-CSRF"] == "1" &&
            (string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, http.Request));
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record UrlRequest(string? DestinationUrl, long Revision);
    private sealed record Scope(Guid CompanyId, string CompanyName,
        Guid BranchId, string BranchName);
    private sealed record ConnectionResponse(Guid CompanyId, string CompanyName,
        Guid BranchId, string BranchName, Guid? InstallationId, string? DestinationUrl,
        bool IsActive, bool HasCredential, bool HasPreviousCredential,
        DateTimeOffset? PreviousCredentialExpiresAtUtc, long Revision);
    private sealed record ConnectionBundle(int Version, string Label, string DestinationUrl,
        Guid InstallationId, Guid RemoteCompanyId, Guid RemoteBranchId, string Token);
}
