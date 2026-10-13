using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Organization;

public static class ManufacturerEndpoints
{
    public const string CookieName = "carnicerias.manufacturer";
    private const string CookiePath = "/api/manufacturer";

    public static IEndpointRouteBuilder MapManufacturerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/manufacturer/sessions", LoginAsync);
        var manufacturer = endpoints.MapGroup("/api/manufacturer")
            .AddEndpointFilter<ManufacturerAuthorizationFilter>();
        manufacturer.MapGet("/sessions/current", CurrentAsync);
        manufacturer.MapDelete("/sessions/current", LogoutAsync);
        manufacturer.MapGet("/companies", ListCompaniesAsync);
        manufacturer.MapPost("/companies", CreateCompanyAsync);
        manufacturer.MapPatch("/companies/{companyId:guid}", UpdateCompanyAsync);
        manufacturer.MapPost("/companies/{companyId:guid}/administrators", CreateAdministratorAsync);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        ManufacturerLoginRequest? request, SessionAuthenticationService sessions,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (request is null || string.IsNullOrWhiteSpace(request.Credential) ||
            string.IsNullOrWhiteSpace(request.Password) || request.Credential.Length > 320 ||
            Encoding.UTF8.GetByteCount(request.Password) > 1024)
            return Error(400, "VALIDATION_ERROR");

        var created = await sessions.CreateManufacturerAsync(request.Credential, request.Password,
            cancellationToken);
        if (created is null) return Error(401, "AUTHENTICATION_FAILED");

        httpContext.Response.Cookies.Append(CookieName, created.Credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = created.Session.ExpiresAtUtc
        });
        return Results.Ok(new ManufacturerSessionResponse(created.User.Id, created.User.Username,
            created.Session.ExpiresAtUtc));
    }

    private static async Task<IResult> CurrentAsync(
        SessionAuthenticationService sessions, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var current = await sessions.FindManufacturerActiveAsync(httpContext.Request.Cookies[CookieName],
            cancellationToken);
        return current is null ? Error(401, "NOT_AUTHENTICATED") : Results.Ok(
            new ManufacturerSessionResponse(current.User.Id, current.User.Username,
                current.Session.ExpiresAtUtc));
    }

    private static async Task<IResult> LogoutAsync(
        SessionAuthenticationService sessions, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        await sessions.RevokeAsync(httpContext.Request.Cookies[CookieName], cancellationToken);
        httpContext.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = CookiePath
        });
        return Results.NoContent();
    }

    private static async Task<IResult> ListCompaniesAsync(
        PlatformAccessDbContext db, CancellationToken cancellationToken)
    {
        var companies = await db.Companies.AsNoTracking().OrderBy(company => company.Name)
            .Select(company => new ManufacturerCompanyResponse(company.Id, company.Code, company.Name,
                company.IsActive,
                db.Branches.Count(branch => branch.CompanyId == company.Id && branch.IsActive)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(companies);
    }

    private static async Task<IResult> CreateCompanyAsync(
        CreateCompanyRequest? request, PlatformAccessDbContext db, IPasswordHasher passwordHasher,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name) || !ValidName(request.InitialBranchName) ||
            !ValidUsername(request.AdminUsername) || !ValidEmail(request.AdminEmail) ||
            !VisualDevelopmentPasswordPolicy.IsValid(request.AdminPassword, db.Database.GetConnectionString()))
            return Error(400, "VALIDATION_ERROR");

        Company company;
        try { company = new Company(request.Name, request.Code); }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }

        var adminRoleId = await db.Roles.AsNoTracking().Where(role => role.Code == "administrator")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (adminRoleId is null) return Error(503, "ADMIN_ROLE_UNAVAILABLE");

        var branch = new Branch(company.Id, request.InitialBranchName);
        var admin = UserIdentity.CreateForCompany(company.Id, request.AdminUsername, request.AdminEmail,
            passwordHasher.Hash(request.AdminPassword));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.AddRange(company, branch, admin);
        db.UserAssignments.Add(new UserAssignment(admin.Id, adminRoleId.Value, company.Id));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(409, "COMPANY_OR_USER_ALREADY_EXISTS");
        }

        return Results.Created($"/api/manufacturer/companies/{company.Id}",
            new CreatedCompanyResponse(company.Id, company.Code, company.Name, branch.Id,
                admin.Id));
    }

    private static async Task<IResult> UpdateCompanyAsync(
        Guid companyId, UpdateCompanyRequest? request, PlatformAccessDbContext db,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (companyId == Guid.Empty || request is null ||
            (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(400, "VALIDATION_ERROR");

        var company = await db.Companies.SingleOrDefaultAsync(item => item.Id == companyId,
            cancellationToken);
        if (company is null) return Error(404, "COMPANY_NOT_FOUND");
        if (request.IsActive == false && company.IsActive &&
            (await db.CashierShifts.AnyAsync(shift => shift.CompanyId == companyId &&
                shift.Status == CashierShiftStatus.Open, cancellationToken) ||
             await db.SaleDrafts.AnyAsync(draft => draft.CompanyId == companyId &&
                draft.Status == SaleDraftStatus.Draft, cancellationToken)))
            return Error(409, "COMPANY_HAS_OPEN_OPERATIONS");

        if (request.Name is not null) company.Rename(request.Name);
        if (request.IsActive is true) company.Activate();
        else if (request.IsActive is false) company.Deactivate();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        { return Error(409, "COMPANY_ALREADY_EXISTS"); }

        var activeBranchCount = await db.Branches.CountAsync(branch => branch.CompanyId == companyId &&
            branch.IsActive, cancellationToken);
        return Results.Ok(new ManufacturerCompanyResponse(company.Id, company.Code, company.Name,
            company.IsActive, activeBranchCount));
    }

    private static async Task<IResult> CreateAdministratorAsync(
        Guid companyId, CreateAdministratorRequest? request, PlatformAccessDbContext db,
        IPasswordHasher passwordHasher, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (companyId == Guid.Empty || request is null || !ValidUsername(request.Username) ||
            !ValidEmail(request.Email) ||
            !VisualDevelopmentPasswordPolicy.IsValid(request.Password, db.Database.GetConnectionString()))
            return Error(400, "VALIDATION_ERROR");

        if (!await db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId,
                cancellationToken))
            return Error(404, "COMPANY_NOT_FOUND");
        var roleId = await db.Roles.AsNoTracking().Where(role => role.Code == "administrator")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (roleId is null) return Error(503, "ADMIN_ROLE_UNAVAILABLE");

        var admin = UserIdentity.CreateForCompany(companyId, request.Username, request.Email,
            passwordHasher.Hash(request.Password));
        db.Users.Add(admin);
        db.UserAssignments.Add(new UserAssignment(admin.Id, roleId.Value, companyId));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        { return Error(409, "USER_ALREADY_EXISTS"); }

        return Results.Json(new CreatedAdministratorResponse(admin.Id, admin.Username, admin.Email),
            statusCode: StatusCodes.Status201Created);
    }

    private static bool ValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200;

    private static bool ValidUsername(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Normalize(NormalizationForm.FormKC).Length <= 100 &&
        !value.Trim().Normalize(NormalizationForm.FormKC).Contains('@');

    private static bool ValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 320 &&
        System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var address) &&
        string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);

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

    private sealed record ManufacturerLoginRequest(string? Credential, string? Password);
    private sealed record ManufacturerSessionResponse(Guid UserId, string Username,
        DateTimeOffset ExpiresAtUtc);
    private sealed record CreateCompanyRequest(string Name, string Code, string InitialBranchName,
        string AdminUsername, string AdminEmail, string AdminPassword);
    private sealed record UpdateCompanyRequest(string? Name, bool? IsActive);
    private sealed record CreateAdministratorRequest(string Username, string Email, string Password);
    private sealed record ManufacturerCompanyResponse(Guid Id, string Code, string Name,
        bool IsActive, int ActiveBranchCount);
    private sealed record CreatedCompanyResponse(Guid Id, string Code, string Name,
        Guid InitialBranchId, Guid InitialAdministratorId);
    private sealed record CreatedAdministratorResponse(Guid Id, string Username, string Email);
}
