using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Fiscal;

public static class FiscalDocumentEndpoints
{
    public static IEndpointRouteBuilder MapFiscalDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sales/{saleId:guid}/fiscal-document", GetAsync)
            .RequireOperationalContext();
        endpoints.MapPost("/api/sales/{saleId:guid}/fiscal-document", IssueAsync)
            .RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid saleId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, ArcaSettingsResolver settingsResolver,
        CancellationToken cancellationToken)
    {
        var sale = await FindSaleAsync(db, accessor, saleId, cancellationToken);
        if (sale is null) return Error(404, "SALE_NOT_FOUND");
        var document = await LatestAsync(db, sale, cancellationToken);
        var settings = await settingsResolver.ResolveAsync(sale.CompanyId, cancellationToken,
            includeCertificate: false);
        return Results.Ok(ToResponse(sale, document, settings));
    }

    private static async Task<IResult> IssueAsync(Guid saleId, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, ArcaHomologationTicketProvider provider,
        ArcaSettingsResolver settingsResolver, ArcaWsfeClient wsfe,
        TimeProvider timeProvider, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !RequestOriginValidator.IsAllowed(origin, httpContext.Request))
            return Error(403, "CSRF_REJECTED");
        var sale = await FindSaleAsync(db, accessor, saleId, cancellationToken);
        if (sale is null) return Error(404, "SALE_NOT_FOUND");
        if (sale.DocumentType == SaleDocumentType.NonFiscalTicket)
            return Error(409, "NOT_FISCAL_SALE");
        // Existing authorizations remain readable even if the current credential is unavailable.
        var current = await LatestAsync(db, sale, cancellationToken);
        if (current?.Status == FiscalDocumentStatus.Authorized)
        {
            var publicSettings = await settingsResolver.ResolveAsync(sale.CompanyId,
                cancellationToken, includeCertificate: false);
            return Results.Ok(ToResponse(sale, current, publicSettings));
        }
        ArcaRuntimeSettings settings;
        try { settings = await settingsResolver.ResolveAsync(sale.CompanyId, cancellationToken); }
        catch (System.Security.Cryptography.CryptographicException)
        { return Error(503, "ARCA_CERTIFICATE_UNAVAILABLE"); }
        if (!settings.IsConfigured)
            return Error(503, "ARCA_HOMO_NOT_CONFIGURED");

        // The sale, payments and stock were committed independently; ARCA failure never rolls them back.
        if (current?.Status == FiscalDocumentStatus.Prepared)
        {
            if (current.CreatedAtUtc > timeProvider.GetUtcNow().AddMinutes(-1))
                return Error(409, "FISCAL_REQUEST_IN_PROGRESS");
            // A process may have died after persisting the attempt. Never resend blindly.
            current.RequireReconciliation();
            await db.SaveChangesAsync(CancellationToken.None);
        }
        if (current?.Status == FiscalDocumentStatus.Rejected &&
            current.ErrorCodes?.Split(',').Contains("10016") != true)
            return Results.Ok(ToResponse(sale, current, settings));

        ArcaAccessTicket ticket;
        try { ticket = await provider.GetAsync(settings, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Error(503, "ARCA_AUTH_UNAVAILABLE"); }

        if (current?.Status == FiscalDocumentStatus.NeedsReconciliation)
        {
            try
            {
                var lookup = await wsfe.ConsultAsync(ticket, current.PointOfSale,
                    current.VoucherType, current.Number, cancellationToken);
                if (Matches(current, lookup))
                {
                    // WSFE has no merchant operation ID. On a shared point of sale an
                    // identical-looking invoice could belong to the other team.
                    // Never claim an uncertain CAE as ours without independent proof.
                    return Error(409, "FISCAL_RECONCILIATION_PENDING");
                }
                // The shared point of sale assigned this number to another request.
                current.RejectReconciledCollision();
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return Error(409, "FISCAL_RECONCILIATION_PENDING"); }
        }

        // POS 99 is shared: consult the live last authorized number for *every* attempt.
        for (var retry = 0; retry < 2; retry++)
        {
            long last;
            try
            {
                last = await wsfe.GetLastAuthorizedAsync(ticket, settings.PointOfSale,
                VoucherType(sale), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return Error(503, "ARCA_NUMBER_LOOKUP_FAILED"); }
            if (last >= 99999999) return Error(409, "ARCA_NUMBER_EXHAUSTED");

            ArcaCaeRequest request;
            try
            {
                request = FiscalRequestBuilder.Build(sale, settings.PointOfSale,
                last + 1, ArgentinaToday(timeProvider));
            }
            catch (FiscalSaleNotReadyException exception)
            { return Error(409, exception.Code); }

            var document = new FiscalDocument(sale.CompanyId, sale.Id, settings.IssuerCuit,
                request.PointOfSale, request.VoucherType, request.Number, request.IssueDate,
                request.Total, request.ReceiverDocumentType, request.ReceiverDocumentNumber,
                timeProvider.GetUtcNow(), settings.IssuerName, settings.IssuerAddress,
                settings.IssuerIibb, DateOnly.TryParseExact(settings.IssuerActivityStartDate,
                    "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var activityStartDate)
                    ? activityStartDate : null);
            db.FiscalDocuments.Add(document);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (IsUniqueConflict(exception))
            { return Error(409, "FISCAL_NUMBER_CONFLICT"); }

            ArcaCaeResult result;
            try
            {
                result = await wsfe.RequestCaeAsync(ticket, request, cancellationToken);
            }
            catch (ArcaWsfeRejectionException exception)
            {
                document.Reject(exception.Codes);
                await db.SaveChangesAsync(CancellationToken.None);
                if (exception.Codes.Contains(10016) && retry == 0) continue;
                return Results.Ok(ToResponse(sale, document, settings));
            }
            catch (Exception)
            {
                document.RequireReconciliation();
                await db.SaveChangesAsync(CancellationToken.None);
                return Error(409, "FISCAL_RECONCILIATION_PENDING");
            }
            document.Authorize(result.PointOfSale, result.VoucherType, result.Number,
                result.Cae, result.Expiry, timeProvider.GetUtcNow());
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (DbUpdateException)
            { return Error(503, "FISCAL_AUTHORIZATION_PERSISTENCE_FAILED"); }
            return Results.Ok(ToResponse(sale, document, settings));
        }
        return Error(409, "FISCAL_NUMBER_CONFLICT");
    }

    public static bool Matches(FiscalDocument attempt, ArcaInvoiceLookup lookup) =>
        lookup.Result == "A" && lookup.AuthorizationKind == "CAE" &&
        lookup.AuthorizationCode.Length == 14 && lookup.AuthorizationCode.All(char.IsAsciiDigit) &&
        lookup.AuthorizationExpiry is not null &&
        lookup.PointOfSale == attempt.PointOfSale &&
        lookup.VoucherType == attempt.VoucherType && lookup.Number == attempt.Number &&
        lookup.Total == attempt.Total && lookup.IssueDate == attempt.IssueDate &&
        lookup.ReceiverDocumentType == attempt.ReceiverDocumentType &&
        lookup.ReceiverDocumentNumber == attempt.ReceiverDocumentNumber;

    private static int VoucherType(ConfirmedSale sale) => sale.RecipientTaxStatus is
        SaleRecipientTaxStatus.Registered or SaleRecipientTaxStatus.SmallTaxpayer ? 1 : 6;

    private static DateOnly ArgentinaToday(TimeProvider timeProvider) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires")).DateTime);

    private static async Task<ConfirmedSale?> FindSaleAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, Guid saleId, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        return await db.ConfirmedSales.AsNoTracking().Include(sale => sale.Lines)
            .SingleOrDefaultAsync(sale => sale.Id == saleId && sale.CompanyId == context.CompanyId &&
                sale.BranchId == context.BranchId && sale.CashierId == context.UserId &&
                sale.PosTerminalId == accessor.TerminalId, cancellationToken);
    }

    private static Task<FiscalDocument?> LatestAsync(PlatformAccessDbContext db,
        ConfirmedSale sale, CancellationToken cancellationToken) => db.FiscalDocuments
            .Where(document => document.CompanyId == sale.CompanyId && document.SaleId == sale.Id)
            .OrderByDescending(document => document.CreatedAtUtc)
            .ThenByDescending(document => document.Number)
            .FirstOrDefaultAsync(cancellationToken);

    private static object ToResponse(ConfirmedSale sale, FiscalDocument? document,
        ArcaRuntimeSettings settings) => new
        {
            saleId = sale.Id,
            status = document?.Status.ToString() ?? "NotRequested",
            saleDocumentType = sale.DocumentType.ToString(),
            issuerCuit = document?.IssuerCuit,
            issuerName = document?.IssuerName ?? settings.IssuerName,
            issuerAddress = document?.IssuerAddress ?? settings.IssuerAddress,
            issuerIibb = document?.IssuerName is null ? settings.IssuerIibb : document.IssuerIibb,
            issuerActivityStartDate = document?.IssuerName is null ? settings.IssuerActivityStartDate :
                document.IssuerActivityStartDate?.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
            pointOfSale = document?.PointOfSale,
            voucherType = document?.VoucherType,
            number = document?.Number,
            issueDate = document?.IssueDate,
            total = sale.Total,
            receiverName = sale.RecipientName,
            receiverAddress = sale.RecipientAddress,
            receiverTaxStatus = sale.RecipientTaxStatus switch
            {
                SaleRecipientTaxStatus.FinalConsumer => "finalConsumer",
                SaleRecipientTaxStatus.Registered => "registered",
                SaleRecipientTaxStatus.SmallTaxpayer => "smallTaxpayer",
                SaleRecipientTaxStatus.Exempt => "exempt",
                _ => throw new InvalidOperationException("Unsupported recipient tax status")
            },
            receiverDocumentType = document?.ReceiverDocumentType,
            receiverDocumentNumber = document?.ReceiverDocumentNumber,
            vatBreakdown = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.Taxed)
            .GroupBy(line => line.TaxRatePercent)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                ratePercent = group.Key,
                taxableBase = group.Sum(line => line.TaxableBase ?? 0),
                taxAmount = group.Sum(line => line.TaxAmount ?? 0)
            }).ToArray(),
            exemptAmount = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.Exempt)
            .Sum(line => line.NetAfterDiscount ?? 0),
            notTaxedAmount = sale.Lines.Where(line => line.TaxTreatment == SaleTaxTreatment.NotTaxed)
            .Sum(line => line.NetAfterDiscount ?? 0),
            cae = document?.Cae,
            caeExpiry = document?.CaeExpiry,
            errorCodes = document?.ErrorCodes
        };

    private static bool IsUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = new { code, message = "Emisión ARCA no disponible" } }, statusCode: status);
}
