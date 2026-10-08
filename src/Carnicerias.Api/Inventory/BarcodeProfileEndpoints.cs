using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Inventory;

public static class BarcodeProfileEndpoints
{
    public static IEndpointRouteBuilder MapBarcodeProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var profiles = endpoints.MapGroup("/api/admin/barcode-layouts")
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryStockManage);
        profiles.MapGet("", ListAsync);
        profiles.MapPost("", CreateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor, CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        var profiles = await db.BarcodeProfiles.AsNoTracking()
            .Where(profile => profile.CompanyId == companyId)
            .OrderBy(profile => profile.Name).ThenByDescending(profile => profile.Revision)
            .Select(profile => new ProfileResponse(profile.Id, profile.Name, profile.Revision,
                profile.Formula, profile.WeightField, profile.WeightDecimals, profile.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(profiles);
    }

    private static async Task<IResult> CreateAsync(ProfileRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor, HttpContext httpContext, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var normalizedName = request.Name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (normalizedName.Length > 120)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        var revision = (await db.BarcodeProfiles.AsNoTracking()
            .Where(profile => profile.CompanyId == companyId && profile.NormalizedName == normalizedName)
            .Select(profile => (int?)profile.Revision).MaxAsync(cancellationToken) ?? 0) + 1;
        BarcodeProfile profile;
        try
        {
            profile = new BarcodeProfile(companyId, request.Name, revision, request.Formula,
                request.WeightField, request.WeightDecimals, clock.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE_LAYOUT");
        }
        db.BarcodeProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Error(StatusCodes.Status409Conflict, "BARCODE_PROFILE_REVISION_CONFLICT");
        }
        return Results.Created($"/api/admin/barcode-layouts/{profile.Id}",
            new ProfileResponse(profile.Id, profile.Name, profile.Revision,
                profile.Formula, profile.WeightField, profile.WeightDecimals, profile.CreatedAtUtc));
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo guardar el perfil de etiqueta", [])),
        statusCode: statusCode);

    private sealed record ProfileRequest(string Name, string Formula, string WeightField, int WeightDecimals);
    private sealed record ProfileResponse(Guid Id, string Name, int Revision, string Formula,
        string WeightField, int WeightDecimals, DateTime CreatedAtUtc);
}
