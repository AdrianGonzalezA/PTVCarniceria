using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

namespace Carnicerias.Api.Catalog;

public static class AdminPriceListEndpoints
{
    public static IEndpointRouteBuilder MapAdminPriceListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var lists = endpoints.MapGroup("/api/admin/price-lists")
            .RequireOperationalPermission(PlatformPermissionCatalog.CatalogManage);
        lists.MapGet("", ListAsync);
        lists.MapPost("", CreateAsync);
        lists.MapPatch("/{listId:guid}", UpdateAsync);
        lists.MapGet("/{listId:guid}/branches", BranchesAsync);
        lists.MapPut("/{listId:guid}/branches/{branchId:guid}", AssignBranchAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? search = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (search?.Length ?? 0) > 100 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var lists = db.PriceLists.AsNoTracking().Where(list => list.CompanyId == companyId);
        var normalizedSearch = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            lists = lists.Where(list => EF.Functions.ILike(list.Name, pattern, "\\"));
        }

        var totalItems = await lists.LongCountAsync(cancellationToken);
        var items = await lists.OrderBy(list => list.Name).ThenBy(list => list.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(list => new PriceListResponse(list.Id, list.Name, list.IsActive,
                db.BranchPriceLists.Count(link => link.CompanyId == companyId &&
                    link.PriceListId == list.Id && link.IsActive &&
                    db.Branches.Any(branch => branch.CompanyId == companyId &&
                        branch.Id == link.BranchId && branch.IsActive)),
                db.ProductPrices.Count(price => price.CompanyId == companyId &&
                    price.PriceListId == list.Id && price.EffectiveToUtc == null)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new PriceListPage(items, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (totalItems - 1) / pageSize + 1));
    }

    private static async Task<IResult> CreateAsync(
        PriceListCreateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || !ValidName(request.Name))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var list = new PriceList(contextAccessor.Context.CompanyId, request.Name);
        db.PriceLists.Add(list);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "PRICE_LIST_ALREADY_EXISTS");
        }
        return Results.Created($"/api/admin/price-lists/{list.Id}",
            new PriceListResponse(list.Id, list.Name, true, 0, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid listId,
        PriceListUpdateRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null || (request.Name is null && request.IsActive is null) ||
            (request.Name is not null && !ValidName(request.Name)))
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var list = await db.PriceLists.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == listId, cancellationToken);
        if (list is null) return Error(StatusCodes.Status404NotFound, "PRICE_LIST_NOT_FOUND");
        if (request.IsActive == false && list.IsActive &&
            await db.SaleDrafts.AnyAsync(draft => draft.CompanyId == companyId &&
                draft.PriceListId == listId && draft.Status == SaleDraftStatus.Draft, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PRICE_LIST_HAS_OPEN_DRAFTS");

        if (request.Name is not null) list.Rename(request.Name);
        if (request.IsActive is bool isActive)
        {
            if (isActive) list.Activate();
            else list.Deactivate();
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Error(StatusCodes.Status409Conflict, "PRICE_LIST_ALREADY_EXISTS");
        }

        return Results.Ok(new PriceListResponse(list.Id, list.Name, list.IsActive,
            await db.BranchPriceLists.CountAsync(link => link.CompanyId == companyId &&
                link.PriceListId == listId && link.IsActive &&
                db.Branches.Any(branch => branch.CompanyId == companyId &&
                    branch.Id == link.BranchId && branch.IsActive), cancellationToken),
            await db.ProductPrices.CountAsync(price => price.CompanyId == companyId &&
                price.PriceListId == listId && price.EffectiveToUtc == null, cancellationToken)));
    }

    private static async Task<IResult> BranchesAsync(
        Guid listId,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var companyId = contextAccessor.Context.CompanyId;
        if (!await db.PriceLists.AnyAsync(list => list.CompanyId == companyId && list.Id == listId,
                cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PRICE_LIST_NOT_FOUND");

        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branch.CompanyId == companyId)
            .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
            .Select(branch => new BranchAssignmentResponse(branch.Id, branch.Name, branch.IsActive,
                db.BranchPriceLists.Any(link => link.CompanyId == companyId &&
                    link.BranchId == branch.Id && link.PriceListId == listId && link.IsActive)))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(branches);
    }

    private static async Task<IResult> AssignBranchAsync(
        Guid listId,
        Guid branchId,
        BranchAssignmentRequest? request,
        PlatformAccessDbContext db,
        OperationalContextAccessor contextAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var companyId = contextAccessor.Context.CompanyId;
        var list = await db.PriceLists.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == listId, cancellationToken);
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == branchId, cancellationToken);
        if (list is null || branch is null)
            return Error(StatusCodes.Status404NotFound, "PRICE_LIST_OR_BRANCH_NOT_FOUND");
        if (request.IsActive && (!list.IsActive || !branch.IsActive))
            return Error(StatusCodes.Status409Conflict, "PRICE_LIST_OR_BRANCH_INACTIVE");

        var assignment = await db.BranchPriceLists.SingleOrDefaultAsync(link =>
            link.CompanyId == companyId && link.PriceListId == listId && link.BranchId == branchId,
            cancellationToken);
        if (!request.IsActive && assignment is { IsActive: true } &&
            await db.SaleDrafts.AnyAsync(draft => draft.CompanyId == companyId &&
                draft.BranchId == branchId && draft.PriceListId == listId &&
                draft.Status == SaleDraftStatus.Draft, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PRICE_LIST_HAS_OPEN_DRAFTS");

        if (assignment is null && request.IsActive)
        {
            assignment = new BranchPriceList(companyId, branchId, listId);
            db.BranchPriceLists.Add(assignment);
        }
        else if (assignment is not null)
        {
            if (request.IsActive) assignment.Activate();
            else assignment.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new BranchAssignmentResponse(branch.Id, branch.Name, branch.IsActive,
            assignment?.IsActive ?? false));
    }

    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 160;

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

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

    private sealed record PriceListCreateRequest(string Name);
    private sealed record PriceListUpdateRequest(string? Name, bool? IsActive);
    private sealed record BranchAssignmentRequest(bool IsActive);
    private sealed record PriceListResponse(
        Guid Id, string Name, bool IsActive, int ActiveBranchCount, int CurrentPriceCount);
    private sealed record PriceListPage(
        IReadOnlyList<PriceListResponse> Items, int Page, int PageSize, long TotalItems, long TotalPages);
    private sealed record BranchAssignmentResponse(
        Guid BranchId, string BranchName, bool BranchActive, bool IsAssigned);
}
