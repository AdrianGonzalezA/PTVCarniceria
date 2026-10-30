using Carnicerias.Api.Contracts;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Organization;

public static class AdminBranchDeviceEndpoints
{
    public static IEndpointRouteBuilder MapAdminBranchDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var devices = endpoints.MapGroup(
                "/api/admin/companies/{companyId:guid}/branches/{branchId:guid}/devices")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        devices.MapGet("", ListAsync);
        devices.MapPost("", CreateAsync);
        devices.MapPut("/{deviceId:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId,
        PlatformAccessDbContext db, OperationalContextAccessor context,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(db, context.Context.UserId, companyId, branchId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        var devices = await db.BranchDevices.AsNoTracking()
            .Where(device => device.CompanyId == companyId && device.BranchId == branchId)
            .OrderBy(device => device.Type).ThenBy(device => device.Name)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(devices.Select(Response).ToArray());
    }

    private static async Task<IResult> CreateAsync(Guid companyId, Guid branchId,
        DeviceRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor context, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await AuthorizedAsync(db, context.Context.UserId, companyId, branchId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        BranchDevice device;
        try
        {
            device = new BranchDevice(companyId, branchId, request.Name, request.Type,
                request.Driver, request.Port, request.BaudRate, request.PaperWidthMm);
            device.SetActive(request.IsActive);
        }
        catch (ArgumentException) { return Error(StatusCodes.Status400BadRequest, "DEVICE_CONFIG_INVALID"); }
        db.BranchDevices.Add(device);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (UniqueViolation(exception))
        { return Error(StatusCodes.Status409Conflict, "DEVICE_NAME_ALREADY_EXISTS"); }
        http.Response.Headers.CacheControl = "no-store";
        return Results.Created($"/api/admin/companies/{companyId}/branches/{branchId}/devices/{device.Id}",
            Response(device));
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid branchId, Guid deviceId,
        DeviceRequest? request, PlatformAccessDbContext db,
        OperationalContextAccessor context, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(http)) return Error(StatusCodes.Status403Forbidden, "CSRF_REJECTED");
        if (request is null) return Error(StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
        if (!await AuthorizedAsync(db, context.Context.UserId, companyId, branchId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "BRANCH_NOT_FOUND");
        var device = await db.BranchDevices.SingleOrDefaultAsync(item => item.Id == deviceId &&
            item.CompanyId == companyId && item.BranchId == branchId, cancellationToken);
        if (device is null) return Error(StatusCodes.Status404NotFound, "DEVICE_NOT_FOUND");

        var assigned = await db.PosTerminals.Where(terminal => terminal.CompanyId == companyId &&
            terminal.BranchId == branchId &&
            (terminal.ScaleDeviceId == deviceId || terminal.PrinterDeviceId == deviceId))
            .ToArrayAsync(cancellationToken);
        if (!request.IsActive && assigned.Length > 0)
            return Error(StatusCodes.Status409Conflict, "DEVICE_ASSIGNED_TO_TERMINAL");
        if (assigned.Length > 0 && (request.Type != device.Type || request.Driver != device.Driver))
            return Error(StatusCodes.Status409Conflict, "DEVICE_DRIVER_IN_USE");
        try
        {
            device.Configure(request.Name, request.Type, request.Driver, request.Port,
                request.BaudRate, request.PaperWidthMm);
            device.SetActive(request.IsActive);
            var otherIds = assigned.Select(terminal => terminal.ScaleDeviceId == deviceId
                    ? terminal.PrinterDeviceId : terminal.ScaleDeviceId)
                .Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
            var others = await db.BranchDevices.Where(item => otherIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            foreach (var terminal in assigned)
            {
                BranchDevice? other = null;
                var otherId = terminal.ScaleDeviceId == deviceId
                    ? terminal.PrinterDeviceId : terminal.ScaleDeviceId;
                if (otherId is not null) others.TryGetValue(otherId.Value, out other);
                terminal.AssignCatalogDevices(terminal.ScaleDeviceId == deviceId ? device : other,
                    terminal.PrinterDeviceId == deviceId ? device : other);
            }
        }
        catch (ArgumentException) { return Error(StatusCodes.Status400BadRequest, "DEVICE_CONFIG_INVALID"); }
        catch (InvalidOperationException) { return Error(StatusCodes.Status409Conflict, "DEVICE_CONFIG_CONFLICT"); }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (UniqueViolation(exception))
        { return Error(StatusCodes.Status409Conflict, "DEVICE_NAME_ALREADY_EXISTS"); }
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(Response(device));
    }

    private static DeviceResponse Response(BranchDevice device) => new(device.Id, device.Name,
        device.Type, device.Driver, device.Port, device.BaudRate, device.PaperWidthMm,
        device.IsActive);

    private static Task<bool> AuthorizedAsync(PlatformAccessDbContext db, Guid userId,
        Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        db.Branches.AnyAsync(branch => branch.CompanyId == companyId && branch.Id == branchId &&
            db.Users.Any(user => user.Id == userId && user.CompanyId == companyId &&
                user.IsActive && !user.IsManufacturer) &&
            db.UserAssignments.Any(assignment => assignment.UserId == userId &&
                assignment.CompanyId == companyId && db.Roles.Any(role =>
                    role.Id == assignment.RoleId && role.Code == "administrator")), cancellationToken);

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static bool UniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult Error(int statusCode, string code) => Results.Json(
        new ErrorResponse(new ApiError(code, "No se pudo completar la solicitud", [])),
        statusCode: statusCode);

    private sealed record DeviceRequest(string Name, string Type, string Driver,
        string Port, int BaudRate, int? PaperWidthMm, bool IsActive = true);
    private sealed record DeviceResponse(Guid Id, string Name, string Type, string Driver,
        string Port, int BaudRate, int? PaperWidthMm, bool IsActive);
}
