using System.Data;
using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Catalog;

public static class AdminTaxCatalogEndpoints
{
    private const int PageSize = 25;

    public static IEndpointRouteBuilder MapAdminTaxCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/taxes")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        group.MapGet("", ListAsync);
        group.MapGet("/options", OptionsAsync);
        group.MapGet("/active", ActiveAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{taxId:guid}", DeactivateAsync);
        return endpoints;
    }

    private static async Task<IResult> OptionsAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        var rows = await db.TaxCatalogEntries.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.IsActive && item.Kind == TaxKind.Vat)
            .OrderBy(item => item.RatePercent).ThenBy(item => item.Code)
            .Select(item => new TaxOption(item.Id, item.Code, item.Name, item.RatePercent))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ActiveAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        var entries = await db.TaxCatalogEntries.AsNoTracking().Where(item =>
                item.CompanyId == companyId && item.IsActive)
            .OrderBy(item => item.Kind).ThenBy(item => item.Code)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(entries.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken,
        int page = 1, string? search = null)
    {
        if (page is < 1 or > 10_000 || (search?.Length ?? 0) > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        var entries = db.TaxCatalogEntries.AsNoTracking().Where(item => item.CompanyId == companyId);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            entries = entries.Where(item => EF.Functions.ILike(item.Code, pattern, "\\") ||
                EF.Functions.ILike(item.Name, pattern, "\\"));
        }
        var total = await entries.CountAsync(cancellationToken);
        var rows = await entries.OrderBy(item => item.Kind).ThenBy(item => item.Code)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new TaxCatalogPage(page, PageSize, total,
            rows.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> CreateAsync(CreateTaxRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || request.RatePercent is null ||
            !TryKind(request.Kind, out var kind))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        TaxCatalogEntry entry;
        try
        {
            entry = new TaxCatalogEntry(accessor.Context.CompanyId, request.Code,
                request.Name, kind, request.RatePercent.Value, accessor.Context.UserId,
                timeProvider.GetUtcNow());
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        }
        db.TaxCatalogEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "TAX_CODE_EXISTS");
        }
        return Results.Created($"/api/admin/taxes/{entry.Id}", ToResponse(entry));
    }

    private static async Task<IResult> DeactivateAsync(Guid taxId, SetTaxStatusRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (taxId == Guid.Empty || request?.IsActive is not false)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var companyId = accessor.Context.CompanyId;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await TaxCatalogConsistencyLock.AcquireAsync(db, taxId, cancellationToken);
            var entry = await db.TaxCatalogEntries.SingleOrDefaultAsync(item =>
                item.CompanyId == companyId && item.Id == taxId, cancellationToken);
            if (entry is null) return Results.NotFound();
            if (!entry.IsActive) return Results.Ok(ToResponse(entry));
            if (await db.ProductTaxRules.AsNoTracking().AnyAsync(item =>
                    item.CompanyId == companyId && item.TaxCatalogEntryId == taxId &&
                    item.EffectiveToUtc == null, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "TAX_IN_USE");
            if (await db.OtherTaxAssignments.AsNoTracking().AnyAsync(item =>
                    item.CompanyId == companyId && item.TaxCatalogEntryId == taxId &&
                    item.EffectiveToUtc == null, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "TAX_IN_USE");
            entry.Deactivate(accessor.Context.UserId, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(entry));
        }
        catch (PostgresException exception) when (exception.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            return Error(StatusCodes.Status409Conflict, "TAX_IN_USE");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            return Error(StatusCodes.Status409Conflict, "TAX_IN_USE");
        }
    }

    private static TaxCatalogResponse ToResponse(TaxCatalogEntry entry) => new(
        entry.Id, entry.Code, entry.Name, entry.Kind == TaxKind.Vat ? "iva" : "otro",
        entry.RatePercent, entry.IsActive, entry.CreatedAtUtc, entry.DeactivatedAtUtc);

    private static bool TryKind(string? raw, out TaxKind kind)
    {
        kind = raw?.Trim().ToLowerInvariant() switch
        {
            "iva" => TaxKind.Vat,
            "otro" => TaxKind.Other,
            _ => (TaxKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])), statusCode: statusCode);

    private sealed record CreateTaxRequest(string Code, string Name, string? Kind, decimal? RatePercent);
    private sealed record SetTaxStatusRequest(bool? IsActive);
    private sealed record TaxCatalogResponse(Guid Id, string Code, string Name, string Kind,
        decimal RatePercent, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? DeactivatedAtUtc);
    private sealed record TaxCatalogPage(int Page, int PageSize, int Total,
        IReadOnlyList<TaxCatalogResponse> Items);
    private sealed record TaxOption(Guid Id, string Code, string Name, decimal RatePercent);
}
