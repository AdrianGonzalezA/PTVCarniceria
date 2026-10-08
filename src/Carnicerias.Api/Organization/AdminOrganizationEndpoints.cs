using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Organization;

public static class AdminOrganizationEndpoints
{
    public static IEndpointRouteBuilder MapAdminOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var companies = endpoints.MapGroup("/api/admin/companies")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        companies.MapGet("", ListCompaniesAsync);
        companies.MapPost("", CreateCompanyAsync);
        companies.MapPatch("/{companyId:guid}", UpdateCompanyAsync);
        companies.MapGet("/{companyId:guid}/branches", ListBranchesAsync);
        companies.MapPost("/{companyId:guid}/branches", CreateBranchAsync);
        companies.MapPatch("/{companyId:guid}/branches/{branchId:guid}", UpdateBranchAsync);
        return endpoints;
    }

    private static async Task<IResult> ListCompaniesAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var userId = contextAccessor.Context.UserId;
        var companies = await db.Companies.AsNoTracking()
            .Where(company => db.UserAssignments.Any(assignment =>
                assignment.UserId == userId && assignment.CompanyId == company.Id &&
                db.Roles.Any(role => role.Id == assignment.RoleId && role.Code == "administrator")))
            .OrderBy(company => company.Name).ThenBy(company => company.Id)
            .Select(company => new CompanyResponse(company.Id, company.Name, company.IsActive,
                db.Branches.Count(branch => branch.CompanyId == company.Id && branch.IsActive)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(companies);
    }

    private static async Task<IResult> CreateCompanyAsync(
        CompanyCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name) || !ValidName(request.InitialBranchName))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var roleId = await db.Roles.AsNoTracking()
            .Where(role => role.Code == "administrator")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (roleId is null) return Error(StatusCodes.Status503ServiceUnavailable, "ADMIN_ROLE_UNAVAILABLE");

        var company = new Company(request.Name);
        var branch = new Branch(company.Id, request.InitialBranchName);
        var assignment = new UserAssignment(contextAccessor.Context.UserId, roleId.Value, company.Id);
        db.Companies.Add(company);
        db.Branches.Add(branch);
        db.UserAssignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "ORGANIZATION_NAME_ALREADY_EXISTS");
        }
        return Results.Created($"/api/admin/companies/{company.Id}",
            new CompanyCreatedResponse(company.Id, company.Name, branch.Id, branch.Name));
    }

    private static async Task<IResult> UpdateCompanyAsync(
        Guid companyId,
        CompanyUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = contextAccessor.Context;
        if (!await IsAssignedAdministratorAsync(db, context.UserId, companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "COMPANY_NOT_FOUND");
        var company = await db.Companies.SingleOrDefaultAsync(item => item.Id == companyId,
            cancellationToken);
        if (company is null) return Error(StatusCodes.Status404NotFound, "COMPANY_NOT_FOUND");
        if (request.IsActive == false && company.IsActive)
        {
            if (context.CompanyId == companyId)
                return Error(StatusCodes.Status409Conflict, "CURRENT_COMPANY_CANNOT_BE_INACTIVATED");
            if (await HasOpenOperationsAsync(db, companyId, null, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "COMPANY_HAS_OPEN_OPERATIONS");
        }

        if (request.Name is not null) company.Rename(request.Name);
        if (request.IsActive is bool isActive)
        {
            if (isActive) company.Activate();
            else company.Deactivate();
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "ORGANIZATION_NAME_ALREADY_EXISTS");
        }
        var activeBranchCount = await db.Branches.CountAsync(branch => branch.CompanyId == companyId &&
            branch.IsActive, cancellationToken);
        return Results.Ok(new CompanyResponse(company.Id, company.Name, company.IsActive,
            activeBranchCount));
    }

    private static async Task<IResult> ListBranchesAsync(
        Guid companyId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        if (!await IsAssignedAdministratorAsync(db, contextAccessor.Context.UserId, companyId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "COMPANY_NOT_FOUND");
        var branches = await db.Branches.AsNoTracking().Where(branch => branch.CompanyId == companyId)
            .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
            .Select(branch => new BranchResponse(branch.Id, branch.Name, branch.IsActive,
                db.PosTerminals.Count(terminal => terminal.CompanyId == companyId &&
                    terminal.BranchId == branch.Id && terminal.IsActive)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(branches);
    }

    private static async Task<IResult> CreateBranchAsync(
        Guid companyId,
        BranchCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await IsAssignedAdministratorAsync(db, contextAccessor.Context.UserId, companyId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "COMPANY_NOT_FOUND");
        if (!await db.Companies.AnyAsync(company => company.Id == companyId && company.IsActive,
                cancellationToken))
            return Error(StatusCodes.Status409Conflict, "COMPANY_INACTIVE");

        var branch = new Branch(companyId, request.Name);
        db.Branches.Add(branch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "BRANCH_NAME_ALREADY_EXISTS");
        }
        return Results.Created($"/api/admin/companies/{companyId}/branches/{branch.Id}",
            new BranchResponse(branch.Id, branch.Name, true, 0));
    }

    private static async Task<IResult> UpdateBranchAsync(
        Guid companyId,
        Guid branchId,
        BranchUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var context = contextAccessor.Context;
        if (!await IsAssignedAdministratorAsync(db, context.UserId, companyId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "COMPANY_NOT_FOUND");
        var branch = await db.Branches.SingleOrDefaultAsync(item => item.CompanyId == companyId &&
            item.Id == branchId, cancellationToken);
        if (branch is null) return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        if (request.IsActive == false && branch.IsActive)
        {
            if (context.CompanyId == companyId && context.BranchId == branchId)
                return Error(StatusCodes.Status409Conflict, "CURRENT_BRANCH_CANNOT_BE_INACTIVATED");
            if (await HasOpenOperationsAsync(db, companyId, branchId, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "BRANCH_HAS_OPEN_OPERATIONS");
        }
        if (request.IsActive == true &&
            !await db.Companies.AnyAsync(company => company.Id == companyId && company.IsActive,
                cancellationToken))
            return Error(StatusCodes.Status409Conflict, "COMPANY_INACTIVE");

        if (request.Name is not null) branch.Rename(request.Name);
        if (request.IsActive is bool isActive)
        {
            if (isActive) branch.Activate();
            else branch.Deactivate();
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "BRANCH_NAME_ALREADY_EXISTS");
        }
        var terminalCount = await db.PosTerminals.CountAsync(terminal => terminal.CompanyId == companyId &&
            terminal.BranchId == branchId && terminal.IsActive, cancellationToken);
        return Results.Ok(new BranchResponse(branch.Id, branch.Name, branch.IsActive, terminalCount));
    }

    private static Task<bool> IsAssignedAdministratorAsync(
        PlatformAccessDbContext db, Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        db.UserAssignments.AnyAsync(assignment => assignment.UserId == userId &&
            assignment.CompanyId == companyId && db.Roles.Any(role => role.Id == assignment.RoleId &&
                role.Code == "administrator"), cancellationToken);

    private static async Task<bool> HasOpenOperationsAsync(
        PlatformAccessDbContext db, Guid companyId, Guid? branchId, CancellationToken cancellationToken) =>
        await db.CashierShifts.AnyAsync(shift => shift.CompanyId == companyId &&
            (!branchId.HasValue || shift.BranchId == branchId.Value) &&
            shift.Status == CashierShiftStatus.Open, cancellationToken) ||
        await db.SaleDrafts.AnyAsync(draft => draft.CompanyId == companyId &&
            (!branchId.HasValue || draft.BranchId == branchId.Value) &&
            draft.Status == SaleDraftStatus.Draft, cancellationToken);

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200;

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

    private sealed record CompanyCreateRequest(string Name, string InitialBranchName);
    private sealed record CompanyUpdateRequest(string? Name, bool? IsActive);
    private sealed record BranchCreateRequest(string Name);
    private sealed record BranchUpdateRequest(string? Name, bool? IsActive);
    private sealed record CompanyCreatedResponse(Guid Id, string Name, Guid InitialBranchId, string InitialBranchName);
    private sealed record CompanyResponse(Guid Id, string Name, bool IsActive, int ActiveBranchCount);
    private sealed record BranchResponse(Guid Id, string Name, bool IsActive, int ActiveTerminalCount);
}
