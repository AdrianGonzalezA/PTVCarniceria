using Carnicerias.Api.Security;
using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Api.Fiscal;

public static class FiscalDocumentEndpoints
{
    private static readonly Action<ILogger, Guid, Exception?> LogLockUnavailable =
        LoggerMessage.Define<Guid>(LogLevel.Error, new EventId(3001, "FiscalLockUnavailable"),
            "Fiscal issuance lock unavailable for sale {SaleId}");
    private static readonly Action<ILogger, Guid, long, Exception?> LogCollision =
        LoggerMessage.Define<Guid, long>(LogLevel.Warning, new EventId(3002, "FiscalCollision"),
            "ARCA fiscal number collision reconciled for attempt {AttemptId}, number {Number}");
    private static readonly Action<ILogger, Guid, long, long, Exception?> LogConfirmedAbsent =
        LoggerMessage.Define<Guid, long, long>(LogLevel.Information,
            new EventId(3003, "FiscalAttemptNotIssued"),
            "ARCA confirmed fiscal attempt {AttemptId}, number {Number} was not issued; last authorized {LastAuthorized}");
    private static readonly Action<ILogger, Guid, Exception?> LogReconciliationFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(3004, "FiscalReconciliationFailed"),
            "ARCA fiscal reconciliation failed for attempt {AttemptId}");

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
        TimeProvider timeProvider, ILoggerFactory loggerFactory, HttpContext httpContext,
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
        ArcaAccessTicket ticket;
        try { ticket = await provider.GetAsync(settings, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Error(503, "ARCA_AUTH_UNAVAILABLE"); }
        var logger = loggerFactory.CreateLogger("Carnicerias.FiscalIssuance");

        // Serialise local issuers sharing a CUIT, point and voucher type, including across API processes.
        // The lock also prevents a slow request from being mistaken for an abandoned one.
        FiscalIssuanceLock? issuanceLock;
        try
        {
            issuanceLock = await FiscalIssuanceLock.TryAcquireAsync(db,
                settings.IssuerCuit, settings.PointOfSale, VoucherType(sale), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLockUnavailable(logger, sale.Id, exception);
            return Error(503, "FISCAL_NUMBER_LOCK_UNAVAILABLE");
        }
        await using var heldLock = issuanceLock;
        if (issuanceLock is null) return Error(409, "FISCAL_REQUEST_IN_PROGRESS");
        db.ChangeTracker.Clear();
        current = await LatestAsync(db, sale, cancellationToken);
        if (current?.Status == FiscalDocumentStatus.Authorized)
            return Results.Ok(ToResponse(sale, current, settings));
        if (current?.Status is FiscalDocumentStatus.Prepared or FiscalDocumentStatus.NeedsReconciliation &&
            !IsSameSequence(current, settings.IssuerCuit, settings.PointOfSale, VoucherType(sale)))
            return Error(409, "FISCAL_RECONCILIATION_PENDING");
        if (current?.Status == FiscalDocumentStatus.Prepared)
        {
            if (current.CreatedAtUtc > timeProvider.GetUtcNow().AddMinutes(-1))
                return Error(409, "FISCAL_REQUEST_IN_PROGRESS");
            // A process may have died after persisting the attempt. Never resend blindly.
            current.RequireReconciliation();
            await db.SaveChangesAsync(CancellationToken.None);
        }
        if (current?.Status == FiscalDocumentStatus.Rejected &&
            current.ErrorCodes?.Split(',').Any(code => code is "10016" or "602") != true)
            return Results.Ok(ToResponse(sale, current, settings));

        if (current?.Status == FiscalDocumentStatus.NeedsReconciliation)
        {
            if (!await ReconcileAsync(current, db, wsfe, ticket, timeProvider, logger,
                    cancellationToken))
                return Error(409, "FISCAL_RECONCILIATION_PENDING");
        }

        // A point may be shared with another issuer client: consult ARCA for every attempt.
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

            var blocking = await db.FiscalDocuments.FirstOrDefaultAsync(item =>
                item.IssuerCuit == settings.IssuerCuit &&
                item.PointOfSale == request.PointOfSale &&
                item.VoucherType == request.VoucherType &&
                item.Number == request.Number &&
                item.Status != FiscalDocumentStatus.Rejected, cancellationToken);
            if (blocking is not null)
            {
                if (blocking.Status == FiscalDocumentStatus.Authorized)
                    return Error(409, "FISCAL_NUMBER_CONFLICT");
                if (blocking.Status == FiscalDocumentStatus.Prepared)
                {
                    if (blocking.CreatedAtUtc > timeProvider.GetUtcNow().AddMinutes(-1))
                        return Error(409, "FISCAL_REQUEST_IN_PROGRESS");
                    blocking.RequireReconciliation();
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                if (!await ReconcileAsync(blocking, db, wsfe, ticket, timeProvider, logger,
                        cancellationToken))
                    return Error(409, "FISCAL_RECONCILIATION_PENDING");
            }

            var document = new FiscalDocument(sale.CompanyId, sale.Id, settings.IssuerCuit,
                request.PointOfSale, request.VoucherType, request.Number, request.IssueDate,
                request.Total, request.ReceiverDocumentType, request.ReceiverDocumentNumber,
                timeProvider.GetUtcNow(), settings.IssuerName, settings.IssuerAddress,
                settings.IssuerIibb, DateOnly.TryParseExact(settings.IssuerActivityStartDate,
                    "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var activityStartDate)
                    ? activityStartDate : null);
            document.AttachSnapshot(FiscalSnapshotFactory.Create(document, request));
            db.FiscalDocuments.Add(document);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (IsUniqueConflict(exception))
            {
                db.ChangeTracker.Clear();
                return Error(409, "FISCAL_REQUEST_IN_PROGRESS");
            }

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

    private static async Task<bool> ReconcileAsync(FiscalDocument attempt,
        PlatformAccessDbContext db, ArcaWsfeClient wsfe, ArcaAccessTicket ticket,
        TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken)
    {
        if (attempt.IssuerCuit != ticket.Cuit) return false;
        try
        {
            var lookup = await wsfe.ConsultAsync(ticket, attempt.PointOfSale,
                attempt.VoucherType, attempt.Number, cancellationToken);
            if (Matches(attempt, lookup))
            {
                // WSFE has no merchant operation ID. Identical data on a shared point
                // is insufficient proof that this sale owns the CAE.
                return false;
            }
            if (lookup.Result != "A") return false;
            attempt.RejectReconciledCollision();
            await db.SaveChangesAsync(CancellationToken.None);
            LogCollision(logger, attempt.Id, attempt.Number, null);
            return true;
        }
        catch (ArcaWsfeErrorException exception) when (exception.Codes.SequenceEqual([602]))
        {
            try
            {
                var last = await wsfe.GetLastAuthorizedAsync(ticket, attempt.PointOfSale,
                    attempt.VoucherType, cancellationToken);
                if (!CanConfirmNotIssued(attempt, exception.Codes, last,
                        timeProvider.GetUtcNow())) return false;
                attempt.ConfirmNotIssued();
                await db.SaveChangesAsync(CancellationToken.None);
                LogConfirmedAbsent(logger, attempt.Id, attempt.Number, last, null);
                return true;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogReconciliationFailed(logger, attempt.Id, error);
                return false;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReconciliationFailed(logger, attempt.Id, exception);
            return false;
        }
    }

    public static bool CanConfirmNotIssued(FiscalDocument attempt,
        IReadOnlyList<int> consultationErrorCodes, long lastAuthorized,
        DateTimeOffset observedAtUtc) =>
        attempt.Status == FiscalDocumentStatus.NeedsReconciliation &&
        consultationErrorCodes.Count == 1 && consultationErrorCodes[0] == 602 &&
        lastAuthorized >= 0 && lastAuthorized < attempt.Number &&
        // An interrupted request can still complete remotely after our HTTP timeout.
        attempt.CreatedAtUtc <= observedAtUtc.AddMinutes(-2);

    public static bool IsSameSequence(FiscalDocument attempt,
        string issuerCuit, int pointOfSale, int voucherType) =>
        attempt.IssuerCuit == issuerCuit && attempt.PointOfSale == pointOfSale &&
        attempt.VoucherType == voucherType;

    public static bool Matches(FiscalDocument attempt, ArcaInvoiceLookup lookup) =>
        lookup.Result == "A" && lookup.AuthorizationKind == "CAE" &&
        lookup.AuthorizationCode.Length == 14 && lookup.AuthorizationCode.All(char.IsAsciiDigit) &&
        lookup.AuthorizationExpiry is not null &&
        lookup.PointOfSale == attempt.PointOfSale &&
        lookup.VoucherType == attempt.VoucherType && lookup.Number == attempt.Number &&
        lookup.Total == attempt.Total && lookup.IssueDate == attempt.IssueDate &&
        lookup.ReceiverDocumentType == attempt.ReceiverDocumentType &&
        lookup.ReceiverDocumentNumber == attempt.ReceiverDocumentNumber;

    public static string SnapshotStatus(FiscalDocument? document) => document is null
        ? "notIssued" : document.Snapshot is null ? "legacyIncomplete" : "complete";

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
            .Include(document => document.Snapshot)
            .ThenInclude(snapshot => snapshot!.VatLines)
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
            fiscalSnapshotStatus = SnapshotStatus(document),
            fiscalTaxableBase = document?.Snapshot?.TaxableBase,
            fiscalExemptAmount = document?.Snapshot?.ExemptAmount,
            fiscalNotTaxedAmount = document?.Snapshot?.NotTaxedAmount,
            fiscalVatBreakdown = document?.Snapshot?.VatLines.OrderBy(line => line.ArcaRateCode)
                .Select(line => new
                {
                    arcaRateCode = line.ArcaRateCode,
                    ratePercent = line.RatePercent,
                    taxableBase = line.TaxableBase,
                    taxAmount = line.TaxAmount
                }).ToArray(),
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
