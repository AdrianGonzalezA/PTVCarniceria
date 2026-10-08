using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Domain.Inventory;
using Carnicerias.PlatformAccess;

namespace Carnicerias.Api.Inventory;

public static class BarcodePreviewEndpoints
{
    public static IEndpointRouteBuilder MapBarcodePreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/barcode-layouts/preview", Preview)
            .RequireOperationalPermission(PlatformPermissionCatalog.InventoryStockManage);
        return endpoints;
    }

    private static IResult Preview(PreviewRequest? request, HttpContext httpContext)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
            return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || string.IsNullOrWhiteSpace(request.Formula) || request.Formula.Length > 512 ||
            string.IsNullOrEmpty(request.Code) || request.Code.Length > 80 ||
            string.IsNullOrWhiteSpace(request.WeightField) || request.WeightField.Length > 80 ||
            request.WeightDecimals is < 0 or > 6)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        try
        {
            var layout = BarcodeLayout.Parse(request.Formula);
            var read = layout.Decode(request.Code);
            var weight = read.ScaledDecimal(request.WeightField, request.WeightDecimals);
            if (weight <= 0)
                return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE_WEIGHT");
            return Results.Ok(new PreviewResponse(layout.Length, read.Fields, weight));
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE_LAYOUT");
        }
        catch (KeyNotFoundException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE_LAYOUT");
        }
        catch (FormatException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_BARCODE");
        }
    }

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo interpretar el código", [])),
        statusCode: statusCode);

    private sealed record PreviewRequest(string Formula, string Code, string WeightField, int WeightDecimals);
    private sealed record PreviewResponse(int Length, IReadOnlyDictionary<string, string> Fields, decimal WeightKg);
}
