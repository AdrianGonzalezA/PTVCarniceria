using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Organization;

public static class AdminTerminalEndpoints
{
    public static IEndpointRouteBuilder MapAdminTerminalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var terminals = endpoints.MapGroup(
                "/api/admin/companies/{companyId:guid}/branches/{branchId:guid}/terminals")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        terminals.MapGet("", ListAsync);
        terminals.MapPost("", CreateAsync);
        terminals.MapPatch("/{terminalId:guid}", UpdateAsync);
        terminals.MapPost("/{terminalId:guid}/rotate", RotateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid companyId, Guid branchId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedBranchAsync(db, contextAccessor.Context.UserId, companyId, branchId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        var terminals = await db.PosTerminals.AsNoTracking()
            .Where(terminal => terminal.CompanyId == companyId && terminal.BranchId == branchId)
            .OrderBy(terminal => terminal.Name).ThenBy(terminal => terminal.Id)
            .Select(terminal => new TerminalResponse(terminal.Id, terminal.Name, terminal.IsActive,
                terminal.IsHistorical, terminal.CredentialHash != null))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(terminals);
    }

    private static async Task<IResult> CreateAsync(
        Guid companyId, Guid branchId,
        TerminalCreateRequest? request,
        PlatformAccessDbContext db,
        PosTerminalProvisioningService provisioning,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await IsAuthorizedBranchAsync(db, contextAccessor.Context.UserId, companyId, branchId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        if (!await IsActiveBranchAsync(db, companyId, branchId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "BRANCH_INACTIVE");

        ProvisionedPosTerminal created;
        try
        {
            created = await provisioning.CreateAsync(branchId, request.Name, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Error(StatusCodes.Status409Conflict, "TERMINAL_NAME_OR_BRANCH_UNAVAILABLE");
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "TERMINAL_NAME_ALREADY_EXISTS");
        }
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Created($"/api/admin/companies/{companyId}/branches/{branchId}/terminals/{created.Terminal.Id}",
            new ProvisionedTerminalResponse(created.Terminal.Id, created.Terminal.Name,
                created.Credential.Token));
    }

    private static async Task<IResult> UpdateAsync(
        Guid companyId, Guid branchId, Guid terminalId,
        TerminalUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await IsAuthorizedBranchAsync(db, contextAccessor.Context.UserId, companyId, branchId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        var terminal = await db.PosTerminals.SingleOrDefaultAsync(item => item.Id == terminalId &&
            item.CompanyId == companyId && item.BranchId == branchId, cancellationToken);
        if (terminal is null) return Error(StatusCodes.Status404NotFound, "TERMINAL_NOT_FOUND");
        if (terminal.IsHistorical) return Error(StatusCodes.Status409Conflict, "HISTORICAL_TERMINAL");
        if (request.IsActive == false && terminal.IsActive &&
            await HasOpenOperationsAsync(db, companyId, branchId, terminalId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "TERMINAL_HAS_OPEN_OPERATIONS");
        if (request.IsActive == true && !terminal.IsActive &&
            !await IsActiveBranchAsync(db, companyId, branchId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "BRANCH_INACTIVE");

        if (request.Name is not null) terminal.Rename(request.Name);
        string? newCredential = null;
        if (request.IsActive == false && terminal.IsActive)
        {
            terminal.Deactivate();
            await RevokeSessionsAsync(db, terminalId, timeProvider.GetUtcNow(), cancellationToken);
        }
        else if (request.IsActive == true && !terminal.IsActive)
        {
            var credential = PosTerminalCredential.Issue();
            terminal.ReactivateWithCredentialHash(credential.Hash);
            newCredential = credential.Token;
            await RevokeSessionsAsync(db, terminalId, timeProvider.GetUtcNow(), cancellationToken);
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "TERMINAL_NAME_ALREADY_EXISTS");
        }
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new TerminalUpdateResponse(terminal.Id, terminal.Name, terminal.IsActive,
            terminal.IsHistorical, terminal.CredentialHash != null, newCredential));
    }

    private static async Task<IResult> RotateAsync(
        Guid companyId, Guid branchId, Guid terminalId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (!await IsAuthorizedBranchAsync(db, contextAccessor.Context.UserId, companyId, branchId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        var terminal = await db.PosTerminals.SingleOrDefaultAsync(item => item.Id == terminalId &&
            item.CompanyId == companyId && item.BranchId == branchId, cancellationToken);
        if (terminal is null) return Error(StatusCodes.Status404NotFound, "TERMINAL_NOT_FOUND");
        if (!terminal.IsActive || terminal.IsHistorical)
            return Error(StatusCodes.Status409Conflict, "TERMINAL_NOT_ACTIVE");
        if (await HasOpenOperationsAsync(db, companyId, branchId, terminalId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "TERMINAL_HAS_OPEN_OPERATIONS");

        var credential = PosTerminalCredential.Issue();
        terminal.AssignCredentialHash(credential.Hash);
        await RevokeSessionsAsync(db, terminalId, timeProvider.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ProvisionedTerminalResponse(terminal.Id, terminal.Name, credential.Token));
    }

    private static Task<bool> IsAuthorizedBranchAsync(
        PlatformAccessDbContext db, Guid userId, Guid companyId, Guid branchId,
        CancellationToken cancellationToken) =>
        db.Branches.AnyAsync(branch => branch.CompanyId == companyId && branch.Id == branchId &&
            db.UserAssignments.Any(assignment => assignment.UserId == userId &&
                assignment.CompanyId == companyId && db.Roles.Any(role =>
                    role.Id == assignment.RoleId && role.Code == "administrator")), cancellationToken);

    private static async Task<bool> IsActiveBranchAsync(
        PlatformAccessDbContext db, Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        await db.Branches.AnyAsync(branch => branch.Id == branchId && branch.CompanyId == companyId &&
            branch.IsActive, cancellationToken) &&
        await db.Companies.AnyAsync(company => company.Id == companyId && company.IsActive,
            cancellationToken);

    private static async Task<bool> HasOpenOperationsAsync(
        PlatformAccessDbContext db, Guid companyId, Guid branchId, Guid terminalId,
        CancellationToken cancellationToken) =>
        await db.CashierShifts.AnyAsync(shift => shift.CompanyId == companyId &&
            shift.BranchId == branchId && shift.PosTerminalId == terminalId &&
            shift.Status == CashierShiftStatus.Open, cancellationToken) ||
        await db.SaleDrafts.AnyAsync(draft => draft.CompanyId == companyId &&
            draft.BranchId == branchId && draft.PosTerminalId == terminalId &&
            draft.Status == SaleDraftStatus.Draft, cancellationToken);

    private static async Task RevokeSessionsAsync(
        PlatformAccessDbContext db, Guid terminalId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions.Where(session => session.PosTerminalId == terminalId &&
            session.RevokedAtUtc == null && session.ExpiresAtUtc > now).ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
    }

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;

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

    private sealed record TerminalCreateRequest(string Name);
    private sealed record TerminalUpdateRequest(string? Name, bool? IsActive);
    private sealed record TerminalResponse(Guid Id, string Name, bool IsActive, bool IsHistorical,
        bool HasCredential);
    private sealed record TerminalUpdateResponse(Guid Id, string Name, bool IsActive, bool IsHistorical,
        bool HasCredential, string? NewCredential);
    private sealed record ProvisionedTerminalResponse(Guid Id, string Name, string Credential);
}
