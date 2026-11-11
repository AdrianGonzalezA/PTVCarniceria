using System.Text;
using Carnicerias.Api.Contracts;
using Carnicerias.Api.Fiscal;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Customers;

public static class PosCustomerEndpoints
{
    private static readonly Action<ILogger, string, Exception?> LogFiscalLookupState =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1501, "FiscalLookupState"),
            "Fiscal lookup fallback: {Reason}");
    private static readonly Action<ILogger, string, string, Exception?> LogFiscalLookupFailure =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1502, "FiscalLookupFailure"),
            "Fiscal lookup fallback at {Stage}: {Reason}");

    public static IEndpointRouteBuilder MapPosCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customers/credit-options", OptionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        endpoints.MapGet("/api/customers/account-options", AccountOptionsAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        endpoints.MapGet("/api/customers/fiscal-options", FiscalOptionsAsync)
            .RequireOperationalContext();
        endpoints.MapGet("/api/customers/fiscal-lookup", FiscalLookupAsync)
            .RequireOperationalContext();
        endpoints.MapGet("/api/customers/{customerId:guid}/account", AccountAsync)
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        return endpoints;
    }

    private static async Task<IResult> AccountAsync(
        Guid customerId, PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, int page = 1)
    {
        if (customerId == Guid.Empty || page is < 1 or > 10_000)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var companyId = accessor.Context.CompanyId;
        var customer = await db.CustomerAccounts.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.Id == customerId)
            .Select(item => new { item.Id, item.Code, item.Name, item.IsActive, item.CreditEnabled })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null) return Results.NotFound();

        var charges = db.CustomerSaleCharges.AsNoTracking().Where(charge =>
            charge.CompanyId == companyId && charge.CustomerId == customerId);
        var totalCharges = await charges.SumAsync(charge => (decimal?)charge.Amount, cancellationToken) ?? 0;
        var allocations = db.CustomerCollectionAllocations.AsNoTracking().Where(item =>
            item.CompanyId == companyId && item.CustomerId == customerId && !item.Receipt.IsVoided);
        var totalAllocated = await allocations.SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var creditAvailable = await db.CustomerCollectionReceipts.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CustomerId == customerId && !item.IsVoided)
            .SumAsync(item => (decimal?)item.CreditAmount, cancellationToken) ?? 0;
        creditAvailable -= await db.CustomerCreditApplications.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CustomerId == customerId)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        var count = await charges.CountAsync(cancellationToken);
        var pageCharges = await charges.OrderBy(charge => charge.CreatedAtUtc).ThenBy(charge => charge.Id)
            .Skip((page - 1) * 50).Take(50)
            .Select(charge => new { charge.SaleId, charge.CreatedAtUtc, charge.Amount })
            .ToArrayAsync(cancellationToken);
        var saleIds = pageCharges.Select(item => item.SaleId).ToArray();
        var appliedBySale = await allocations.Where(item => saleIds.Contains(item.SaleId))
            .GroupBy(item => item.SaleId)
            .Select(group => new { SaleId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.SaleId, item => item.Amount, cancellationToken);
        var sales = pageCharges.Select(charge => new AccountSale(charge.SaleId,
            charge.CreatedAtUtc, charge.Amount, charge.Amount - appliedBySale.GetValueOrDefault(charge.SaleId)))
            .ToArray();
        return Results.Ok(new CustomerAccountResponse(customer.Id, customer.Code, customer.Name,
            customer.IsActive, customer.CreditEnabled, totalCharges - totalAllocated,
            creditAvailable, count, page, sales));
    }

    private static async Task<IResult> OptionsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, string? search = null)
    {
        if ((search?.Length ?? 0) > 100)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var query = db.CustomerAccounts.AsNoTracking().Where(customer =>
            customer.CompanyId == accessor.Context.CompanyId && customer.IsActive && customer.CreditEnabled);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Code, pattern, "\\") ||
                EF.Functions.ILike(customer.Name, pattern, "\\"));
        }
        var options = await query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
            .Take(20).Select(customer => new CustomerOption(customer.Id, customer.Code, customer.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(options);
    }

    private static async Task<IResult> AccountOptionsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, string? search = null)
    {
        if ((search?.Length ?? 0) > 100)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);

        var query = db.CustomerAccounts.AsNoTracking().Where(customer =>
            customer.CompanyId == accessor.Context.CompanyId);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Code, pattern, "\\") ||
                EF.Functions.ILike(customer.Name, pattern, "\\"));
        }
        var options = await query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
            .Take(20).Select(customer => new CustomerOption(customer.Id, customer.Code, customer.Name))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(options);
    }

    private static async Task<IResult> FiscalOptionsAsync(
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        CancellationToken cancellationToken, string? search = null)
    {
        if ((search?.Length ?? 0) > 100)
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "No se pudo completar la solicitud", [])), statusCode: StatusCodes.Status400BadRequest);
        var query = db.CustomerAccounts.AsNoTracking().Where(customer =>
            customer.CompanyId == accessor.Context.CompanyId && customer.IsActive &&
            customer.FiscalTaxStatus != null);
        var normalized = search?.Trim().Normalize(NormalizationForm.FormKC);
        if (!string.IsNullOrEmpty(normalized))
        {
            var pattern = $"%{EscapeLike(normalized)}%";
            query = query.Where(customer => EF.Functions.ILike(customer.Code, pattern, "\\") ||
                EF.Functions.ILike(customer.Name, pattern, "\\") ||
                (customer.FiscalDocumentNumber != null &&
                 EF.Functions.ILike(customer.FiscalDocumentNumber, pattern, "\\")));
        }
        var rows = await query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
            .Take(20).ToArrayAsync(cancellationToken);
        return Results.Ok(rows.Select(customer => new FiscalCustomerOption(customer.Id,
            customer.Code, customer.Name, customer.FiscalTaxStatus switch
            {
                SaleRecipientTaxStatus.FinalConsumer => "finalConsumer",
                SaleRecipientTaxStatus.Registered => "registered",
                SaleRecipientTaxStatus.SmallTaxpayer => "smallTaxpayer",
                SaleRecipientTaxStatus.Exempt => "exempt",
                _ => throw new InvalidOperationException("Unknown customer tax status")
            }, customer.FiscalDocumentNumber, customer.FiscalAddress,
            customer.FiscalLocality, customer.FiscalProvince)).ToArray());
    }

    private static async Task<IResult> FiscalLookupAsync(string? cuit,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        ArcaSettingsResolver settingsResolver, ArcaHomologationTicketProvider ticketProvider,
        ArcaRegistryClient registry, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!FiscalCuit.IsValid(cuit))
            return Results.Json(new ErrorResponse(new ApiError("VALIDATION_ERROR",
                "Ingresá un CUIT válido de 11 dígitos", [])),
                statusCode: StatusCodes.Status400BadRequest);

        var local = await db.CustomerAccounts.AsNoTracking().Where(customer =>
                customer.CompanyId == accessor.Context.CompanyId && customer.IsActive &&
                customer.FiscalDocumentNumber == cuit)
            .OrderBy(customer => customer.Id).FirstOrDefaultAsync(cancellationToken);
        if (local is not null && local.FiscalTaxStatus is not null &&
            !string.IsNullOrWhiteSpace(local.Name) &&
            !string.IsNullOrWhiteSpace(local.FiscalAddress))
            return Results.Ok(new FiscalLookupResponse("local", local.Id, local.Code, local.Name,
                cuit!, local.FiscalAddress, local.FiscalLocality, local.FiscalProvince,
                TaxStatus(local.FiscalTaxStatus), null));

        var logger = loggerFactory.CreateLogger("Carnicerias.ArcaFiscalLookup");
        var stage = "settings";
        try
        {
            var settings = await settingsResolver.ResolveAsync(accessor.Context.CompanyId,
                cancellationToken);
            if (!settings.IsConfigured)
            {
                LogFiscalLookupState(logger, "ARCA configuration incomplete", null);
                return ManualFallback(local, cuit!);
            }
            stage = "wsaa";
            var ticket = await ticketProvider.GetAsync(settings,
                ArcaWsaaClient.RegistryService, cancellationToken);
            stage = "registry";
            var lookup = await registry.LookupAsync(ticket, cuit!, cancellationToken);
            if (lookup.Person is not { } person)
            {
                LogFiscalLookupState(logger,
                    $"taxpayer unavailable in homologation registry: {lookup.Unavailability}", null);
                return ManualFallback(local, cuit!, lookup.Unavailability switch
                {
                    ArcaRegistryUnavailability.NotFound =>
                        "El CUIT no figura en el padrón de ARCA homologación. Completá y verificá la ficha manualmente.",
                    ArcaRegistryUnavailability.Inactive =>
                        "ARCA informó que el CUIT no está activo. Verificá la situación fiscal antes de emitir.",
                    ArcaRegistryUnavailability.CuitMismatch =>
                        "ARCA devolvió una ficha para otro CUIT. Verificá los datos antes de emitir.",
                    ArcaRegistryUnavailability.ConstanciaUnavailable =>
                        lookup.Detail is { } detail
                            ? $"ARCA informó: {detail.TrimEnd('.')}. Verificá la ficha manualmente."
                            : "ARCA no pudo emitir una constancia para este CUIT. Verificá la ficha manualmente.",
                    _ => "ARCA devolvió una ficha incompleta. Completá y verificá los datos manualmente."
                });
            }
            return Results.Ok(new FiscalLookupResponse("arca", null, null,
                local?.Name ?? person.Name, cuit!, local?.FiscalAddress ?? person.Address,
                local?.FiscalLocality ?? person.Locality,
                local?.FiscalProvince ?? person.Province,
                TaxStatus(local?.FiscalTaxStatus) ?? person.TaxStatus,
                person.TaxStatus is null || string.IsNullOrWhiteSpace(person.Name) ||
                string.IsNullOrWhiteSpace(person.Address)
                    ? lookup.Detail ?? "ARCA devolvió datos incompletos o ambiguos; verificá la ficha antes de emitir."
                    : null));
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or TaskCanceledException or InvalidDataException or
                InvalidOperationException or System.Xml.XmlException or
                System.Security.Cryptography.CryptographicException or ArcaWsaaFaultException or IOException)
        {
            var reason = error is ArcaWsaaFaultException fault ? $"WSAA_{fault.Code}" :
                error is HttpRequestException http ? $"HTTP_{http.HttpRequestError}" :
                error.GetType().Name;
            LogFiscalLookupFailure(logger, stage, reason, null);
            return ManualFallback(local, cuit!, error is ArcaWsaaFaultException authorization
                ? authorization.Code.EndsWith("coe.notAuthorized", StringComparison.OrdinalIgnoreCase)
                    ? "ARCA no autorizó este certificado para consultar el padrón. Avisá al administrador y completá la ficha manualmente."
                    : authorization.Code.EndsWith("coe.alreadyAuthenticated", StringComparison.OrdinalIgnoreCase)
                        ? "ARCA mantiene un ticket de acceso vigente que esta instancia no pudo recuperar. Reintentá más tarde; el padrón no fue consultado."
                        : null
                : null);
        }
    }

    private static IResult ManualFallback(CustomerAccount? local, string cuit, string? notice = null) =>
        Results.Ok(new FiscalLookupResponse("manual", null, null, local?.Name, cuit,
            local?.FiscalAddress, local?.FiscalLocality, local?.FiscalProvince,
            TaxStatus(local?.FiscalTaxStatus),
            notice ?? "No se obtuvieron datos de ARCA. Completá y verificá la ficha manualmente."));

    private static string? TaxStatus(SaleRecipientTaxStatus? status) => status switch
    {
        SaleRecipientTaxStatus.FinalConsumer => "finalConsumer",
        SaleRecipientTaxStatus.Registered => "registered",
        SaleRecipientTaxStatus.SmallTaxpayer => "smallTaxpayer",
        SaleRecipientTaxStatus.Exempt => "exempt",
        _ => null
    };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record CustomerOption(Guid Id, string Code, string Name);
    private sealed record FiscalCustomerOption(Guid Id, string Code, string Name,
        string FiscalTaxStatus, string? FiscalDocumentNumber, string? FiscalAddress,
        string? FiscalLocality, string? FiscalProvince);
    internal sealed record FiscalLookupResponse(string Source, Guid? CustomerId, string? CustomerCode,
        string? Name, string DocumentNumber, string? Address, string? Locality, string? Province,
        string? TaxStatus, string? Notice);
    private sealed record AccountSale(Guid SaleId, DateTimeOffset ChargedAtUtc,
        decimal OriginalAmount, decimal OutstandingAmount);
    private sealed record CustomerAccountResponse(Guid CustomerId, string CustomerCode, string CustomerName,
        bool IsActive, bool CreditEnabled, decimal TotalDebt, decimal CreditAvailable,
        int SaleCount, int Page, IReadOnlyList<AccountSale> Sales);
}
