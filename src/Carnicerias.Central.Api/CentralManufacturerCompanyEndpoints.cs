using System.Globalization;
using System.Text;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Api;

public static class CentralManufacturerCompanyEndpoints
{
    private const int MaximumRequestBytes = 4096;

    public static void MapCentralManufacturerCompanyEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/manufacturer/companies", ListAsync);
        routes.MapPost("/api/v1/manufacturer/companies", CreateAsync);
        routes.MapGet("/api/v1/manufacturer/companies/{companyId:guid}/administrators",
            ListAdministratorsAsync);
        routes.MapPost("/api/v1/manufacturer/companies/{companyId:guid}/administrators",
            AddAdministratorAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context,
        CentralManufacturerSessionService sessions, ICentralCompanyManagementService management,
        CancellationToken cancellationToken)
    {
        if (await AuthenticateAsync(context, sessions, cancellationToken) is null)
            return Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.");
        if (!TryReadPage(context.Request.Query, out var page, out var pageSize))
            return Error(400, "INVALID_PAGINATION", "Paginación inválida.");
        var result = await management.ListCompaniesAsync(page, pageSize, cancellationToken);
        return Page(result);
    }

    private static async Task<IResult> CreateAsync(HttpContext context,
        CentralManufacturerSessionService sessions, ICentralCompanyManagementService management,
        CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var actor = await AuthenticateAsync(context, sessions, cancellationToken);
        if (actor is null)
            return Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.");
        var request = await CentralJsonRequest.ReadAsync<CreateCompanyRequest>(context.Request,
            MaximumRequestBytes, cancellationToken);
        if (request is null || !ValidCode(request.Code) || !ValidName(request.Name)
            || !ValidUsername(request.InitialAdministratorUsername)
            || !ValidPassword(request.InitialAdministratorPassword))
            return Error(400, "INVALID_COMPANY_REQUEST", "Datos de empresa inválidos.");
        try
        {
            var created = await management.CreateCompanyAsync(request.Code!, request.Name!,
                request.InitialAdministratorUsername!, request.InitialAdministratorPassword!,
                actor.Manufacturer.Id, cancellationToken);
            return Results.Created($"/api/v1/manufacturer/companies/{created.Id}", created);
        }
        catch (ArgumentException)
        { return Error(400, "INVALID_COMPANY_REQUEST", "Datos de empresa inválidos."); }
        catch (CentralManagementConflictException)
        { return Error(409, "COMPANY_ALREADY_EXISTS", "El código de empresa ya existe."); }
    }

    private static async Task<IResult> ListAdministratorsAsync(Guid companyId,
        HttpContext context, CentralManufacturerSessionService sessions,
        ICentralCompanyManagementService management, CancellationToken cancellationToken)
    {
        if (await AuthenticateAsync(context, sessions, cancellationToken) is null)
            return Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.");
        if (!TryReadPage(context.Request.Query, out var page, out var pageSize))
            return Error(400, "INVALID_PAGINATION", "Paginación inválida.");
        var result = await management.ListAdministratorsAsync(companyId, page, pageSize,
            cancellationToken);
        return result is null ? Error(404, "COMPANY_NOT_FOUND", "Empresa central no encontrada.")
            : Page(result);
    }

    private static async Task<IResult> AddAdministratorAsync(Guid companyId,
        HttpContext context, CentralManufacturerSessionService sessions,
        ICentralCompanyManagementService management, CancellationToken cancellationToken)
    {
        if (!CentralRequestSecurity.HasSameOriginAndCsrfHeader(context))
            return Error(403, "ORIGIN_REJECTED", "Origen no autorizado.");
        var actor = await AuthenticateAsync(context, sessions, cancellationToken);
        if (actor is null)
            return Error(401, "SESSION_REQUIRED", "Sesión de SuperAdmin central requerida.");
        var request = await CentralJsonRequest.ReadAsync<CreateAdministratorRequest>(
            context.Request, MaximumRequestBytes, cancellationToken);
        if (companyId == Guid.Empty || request is null || !ValidUsername(request.Username)
            || !ValidPassword(request.Password))
            return Error(400, "INVALID_ADMINISTRATOR_REQUEST", "Datos de administrador inválidos.");
        try
        {
            var created = await management.AddAdministratorAsync(companyId, request.Username!,
                request.Password!, actor.Manufacturer.Id, cancellationToken);
            return created is null
                ? Error(404, "COMPANY_NOT_FOUND", "Empresa central no encontrada.")
                : Results.Created($"/api/v1/manufacturer/companies/{companyId}/administrators/{created.Id}",
                    created);
        }
        catch (ArgumentException)
        { return Error(400, "INVALID_ADMINISTRATOR_REQUEST", "Datos de administrador inválidos."); }
        catch (CentralManagementConflictException)
        { return Error(409, "ADMINISTRATOR_CONFLICT", "No se pudo agregar el administrador."); }
    }

    private static Task<CentralManufacturerSessionContext?> AuthenticateAsync(HttpContext context,
        CentralManufacturerSessionService sessions, CancellationToken cancellationToken) =>
        sessions.AuthenticateAsync(context.Request.Cookies[CentralManufacturerSessionEndpoints.CookieName],
            cancellationToken);

    private static bool ValidCode(string? value) => value is { Length: >= 2 and <= 40 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 200;
    private static bool ValidUsername(string? value) => value is { Length: >= 1 and <= 100 }
        && !value.Contains('@') && !value.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(value);
    private static bool ValidPassword(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length >= 6 && Encoding.UTF8.GetByteCount(value) <= 1024;

    private static bool TryReadPage(IQueryCollection query, out int page, out int pageSize)
    {
        page = 1;
        pageSize = 20;
        if (query.Keys.Any(key => key is not ("page" or "pageSize"))) return false;
        if (query.TryGetValue("page", out var pageValues)
            && (pageValues.Count != 1 || !int.TryParse(pageValues[0],
                NumberStyles.None, CultureInfo.InvariantCulture, out page))) return false;
        if (query.TryGetValue("pageSize", out var sizeValues)
            && (sizeValues.Count != 1 || !int.TryParse(sizeValues[0],
                NumberStyles.None, CultureInfo.InvariantCulture, out pageSize))) return false;
        return page >= 1 && pageSize is >= 1 and <= 100
            && (long)(page - 1) * pageSize <= int.MaxValue;
    }

    private static IResult Page<T>(CentralManagementPage<T> result) => Results.Ok(new
    {
        data = result.Data,
        pagination = new
        {
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount,
            totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize)
        }
    });

    private static IResult Error(int status, string code, string message) => Results.Json(
        new { error = new { code, message, details = (object?)null } }, statusCode: status);

    private sealed record CreateCompanyRequest(string? Code, string? Name,
        string? InitialAdministratorUsername, string? InitialAdministratorPassword);
    private sealed record CreateAdministratorRequest(string? Username, string? Password);
}
