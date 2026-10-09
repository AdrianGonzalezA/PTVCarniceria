using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Api.Catalog;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.ExcelImport;

public static class AdminExcelImportEndpoints
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAdminExcelImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/admin/imports");
        foreach (var kind in new[] { "categories", "products", "price-lists" })
            MapKind(root, kind, PlatformPermissionCatalog.CatalogManage);
        MapKind(root, "customers", PlatformPermissionCatalog.OrganizationManage);
        return endpoints;
    }

    private static void MapKind(RouteGroupBuilder root, string kind, string permission)
    {
        var group = root.MapGroup($"/{kind}").RequireOperationalPermission(permission);
        group.MapGet("/template", () => Results.File(AdminExcelWorkbook.CreateTemplate(kind),
            XlsxContentType, $"plantilla-{kind}.xlsx"));
        group.MapPost("/preview", (HttpContext httpContext, PlatformAccessDbContext db,
                OperationalContextAccessor accessor, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PreviewAsync(kind, httpContext, db, accessor, timeProvider, cancellationToken));
        group.MapPost("/apply", (HttpContext httpContext, PlatformAccessDbContext db,
                OperationalContextAccessor accessor, TimeProvider timeProvider, CancellationToken cancellationToken) =>
            ApplyAsync(kind, httpContext, db, accessor, timeProvider, cancellationToken));
    }

    private static async Task<IResult> PreviewAsync(string kind, HttpContext httpContext,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        var payload = await ReadPayloadAsync(httpContext.Request, cancellationToken);
        if (payload is null) return Error(StatusCodes.Status413PayloadTooLarge, "IMPORT_TOO_LARGE");
        if (!ValidContentType(httpContext.Request)) return Error(StatusCodes.Status400BadRequest, "INVALID_CONTENT_TYPE");
        var parsed = AdminExcelWorkbook.Read(kind, payload);
        var result = await AdminExcelImportProcessor.AssessAsync(db, accessor.Context.CompanyId,
            accessor.Context.UserId, kind, parsed, timeProvider.GetUtcNow(), false, cancellationToken);
        return Results.Json(result, statusCode: result.Issues.Count == 0 ? StatusCodes.Status200OK :
            StructuralError(result) ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ApplyAsync(string kind, HttpContext httpContext,
        PlatformAccessDbContext db, OperationalContextAccessor accessor, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (!Guid.TryParse(httpContext.Request.Headers["Idempotency-Key"].ToString(), out var operationId) ||
            operationId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED");
        var payload = await ReadPayloadAsync(httpContext.Request, cancellationToken);
        if (payload is null) return Error(StatusCodes.Status413PayloadTooLarge, "IMPORT_TOO_LARGE");
        if (!ValidContentType(httpContext.Request)) return Error(StatusCodes.Status400BadRequest, "INVALID_CONTENT_TYPE");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var context = accessor.Context;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                cancellationToken);
            var existing = await db.AdminImportOperations.AsNoTracking().SingleOrDefaultAsync(operation =>
                operation.CompanyId == context.CompanyId && operation.OperationId == operationId,
                cancellationToken);
            if (existing is not null)
            {
                if (existing.Kind != kind || existing.RequestHash != hash)
                    return Error(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED");
                return Results.Content(existing.ResultJson, "application/json");
            }

            if (kind == "products")
                await ProductCodeReservation.AcquireCompanyLockAsync(db, context.CompanyId, cancellationToken);

            var parsed = AdminExcelWorkbook.Read(kind, payload);
            var result = await AdminExcelImportProcessor.AssessAsync(db, context.CompanyId, context.UserId,
                kind, parsed, timeProvider.GetUtcNow(), true, cancellationToken);
            if (!result.CanApply)
                return Results.Json(result, statusCode: result.Issues.Count == 0 ?
                    StatusCodes.Status400BadRequest : StructuralError(result) ?
                    StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict);

            await db.SaveChangesAsync(cancellationToken);
            db.AdminImportOperations.Add(new AdminImportOperation(context.CompanyId, operationId,
                context.UserId, kind, hash, JsonSerializer.Serialize(result, JsonOptions), timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(result);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or
                PostgresErrorCodes.SerializationFailure })
        {
            return Error(StatusCodes.Status409Conflict, "IMPORT_CONFLICT_RETRY_PREVIEW");
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or
            PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure)
        {
            return Error(StatusCodes.Status409Conflict, "IMPORT_CONFLICT_RETRY_PREVIEW");
        }
    }

    private static bool StructuralError(ImportPreviewResult result) => result.Issues.Any(issue =>
        issue.Code is "INVALID_XLSX" or "TEMPLATE_VERSION" or "UNKNOWN_SHEET" or "MISSING_SHEET" or
            "UNKNOWN_COLUMN" or "DUPLICATE_COLUMN" or "MISSING_COLUMN" or "FORMULA_NOT_ALLOWED" or
            "INVALID_CELL_TYPE" or "TOO_MANY_ROWS");

    private static bool ValidContentType(HttpRequest request) =>
        request.ContentType?.Split(';', 2)[0].Trim().Equals(XlsxContentType,
            StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<byte[]?> ReadPayloadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > AdminExcelWorkbook.MaxFileBytes) return null;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > AdminExcelWorkbook.MaxFileBytes) return null;
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la importación", [])),
        statusCode: statusCode);
}
