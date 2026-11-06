using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Production;

public static class ProductionClassifierEndpoints
{
    public static IEndpointRouteBuilder MapProductionClassifierEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/production/classifiers", ListAsync)
            .RequireOperationalContext();
        endpoints.MapPost("/api/production/classifiers", CreateAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        endpoints.MapPatch("/api/production/classifiers/{id:guid}", UpdateAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        bool includeInactive = false)
    {
        var companyId = accessor.Context.CompanyId;
        var rows = await db.ProductionClassifiers.AsNoTracking()
            .Where(item => item.CompanyId == companyId &&
                (includeInactive || item.IsActive))
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new ClassifierResponse(item.Id, item.Name, item.IsActive))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(ClassifierRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (!ValidName(request?.Name)) return Error(400, "VALIDATION_ERROR");
        var item = new ProductionClassifier(accessor.Context.CompanyId, request!.Name);
        db.ProductionClassifiers.Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        { return Error(409, "PRODUCTION_CLASSIFIER_DUPLICATE"); }
        return Results.Created($"/api/production/classifiers/{item.Id}",
            new ClassifierResponse(item.Id, item.Name, item.IsActive));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateClassifierRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Name is null && request.IsActive is null ||
            request.Name is not null && !ValidName(request.Name))
            return Error(400, "VALIDATION_ERROR");
        var item = await db.ProductionClassifiers.SingleOrDefaultAsync(row =>
            row.CompanyId == accessor.Context.CompanyId && row.Id == id,
            cancellationToken);
        if (item is null) return Error(404, "PRODUCTION_CLASSIFIER_NOT_FOUND");
        if (request.Name is not null) item.Rename(request.Name);
        if (request.IsActive == true) item.Activate();
        if (request.IsActive == false) item.Deactivate();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        { return Error(409, "PRODUCTION_CLASSIFIER_DUPLICATE"); }
        return Results.Ok(new ClassifierResponse(item.Id, item.Name, item.IsActive));
    }

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool AllowedOrigin(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) ||
            RequestOriginValidator.IsAllowed(origin, http.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: status);

    private sealed record ClassifierRequest(string Name);
    private sealed record UpdateClassifierRequest(string? Name, bool? IsActive);
    private sealed record ClassifierResponse(Guid Id, string Name, bool IsActive);
}
