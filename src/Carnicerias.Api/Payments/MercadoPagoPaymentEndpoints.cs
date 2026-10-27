using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Carnicerias.Infrastructure;
using Carnicerias.Api.Security;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public static class MercadoPagoPaymentEndpoints
{
    private static readonly Action<ILogger, Guid, string, int?, string, Exception?> LogCancelFailure =
        LoggerMessage.Define<Guid, string, int?, string>(LogLevel.Warning,
            new EventId(1001, "MercadoPagoCancelFailed"),
            "MercadoPagoCancelFailed IntentId={IntentId} ErrorType={ErrorType} ProviderStatus={ProviderStatus} TraceId={TraceId}");

    public static IEndpointRouteBuilder MapMercadoPagoPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sales/drafts/{draftId:guid}/mercado-pago");
        group.MapGet("", (Guid draftId, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
            GetAsync(draftId, null, db, accessor, cancellationToken)).RequireOperationalContext();
        group.MapPost("", (Guid draftId, StartRequest? request, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, MercadoPagoSettingsResolver settings,
            MercadoPagoPointClient point, MercadoPagoQrClient qr, MercadoPagoStaticQrClient staticQr,
            MercadoPagoOrderLookupClient lookup, TimeProvider clock, HttpContext context,
            CancellationToken cancellationToken) => StartAsync(draftId, null, request, db, accessor,
                settings, point, qr, staticQr, lookup, clock, context, cancellationToken))
            .RequireOperationalContext();
        group.MapPost("/{intentId:guid}/check", (Guid draftId, Guid intentId,
            PlatformAccessDbContext db, OperationalContextAccessor accessor,
            MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
            MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup, TimeProvider clock,
            HttpContext context, CancellationToken cancellationToken) =>
            CheckAsync(draftId, null, intentId, db, accessor, settings, point, qr, lookup,
                clock, context, cancellationToken)).RequireOperationalContext();
        group.MapPost("/{intentId:guid}/cancel", (Guid draftId, Guid intentId,
            PlatformAccessDbContext db, OperationalContextAccessor accessor,
            MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
            MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup, TimeProvider clock,
            HttpContext context, CancellationToken cancellationToken) =>
            CancelAsync(draftId, null, intentId, db, accessor, settings, point, qr, lookup,
                clock, context, cancellationToken)).RequireOperationalContext();

        var collections = endpoints.MapGroup(
            "/api/customers/collections/operations/{operationId:guid}/mercado-pago");
        collections.MapGet("", (Guid operationId, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
            GetAsync(null, operationId, db, accessor, cancellationToken))
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        collections.MapPost("", (Guid operationId, StartRequest? request, PlatformAccessDbContext db,
            OperationalContextAccessor accessor, MercadoPagoSettingsResolver settings,
            MercadoPagoPointClient point, MercadoPagoQrClient qr, MercadoPagoStaticQrClient staticQr,
            MercadoPagoOrderLookupClient lookup, TimeProvider clock, HttpContext context,
            CancellationToken cancellationToken) => StartAsync(null, operationId, request, db, accessor,
                settings, point, qr, staticQr, lookup, clock, context, cancellationToken))
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        collections.MapPost("/{intentId:guid}/check", (Guid operationId, Guid intentId,
            PlatformAccessDbContext db, OperationalContextAccessor accessor,
            MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
            MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup, TimeProvider clock,
            HttpContext context, CancellationToken cancellationToken) =>
            CheckAsync(null, operationId, intentId, db, accessor, settings, point, qr, lookup,
                clock, context, cancellationToken))
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        collections.MapPost("/{intentId:guid}/cancel", (Guid operationId, Guid intentId,
            PlatformAccessDbContext db, OperationalContextAccessor accessor,
            MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
            MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup, TimeProvider clock,
            HttpContext context, CancellationToken cancellationToken) =>
            CancelAsync(null, operationId, intentId, db, accessor, settings, point, qr, lookup,
                clock, context, cancellationToken))
            .RequireOperationalPermission(PlatformPermissionCatalog.PosAccountCharge);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid? draftId, Guid? operationId,
        PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken)
    {
        if (draftId is Guid saleDraftId &&
            !await OwnsDraftAsync(saleDraftId, db, accessor, cancellationToken) ||
            operationId is not null && accessor.TerminalId is null)
            return Error(404, "DRAFT_NOT_FOUND");
        var intent = await db.PointPaymentIntents.AsNoTracking()
            .Where(item => item.CompanyId == accessor.Context.CompanyId &&
                item.BranchId == accessor.Context.BranchId &&
                item.CashierId == accessor.Context.UserId && item.PosTerminalId == accessor.TerminalId &&
                item.SaleDraftId == draftId && item.CustomerCollectionOperationId == operationId &&
                item.Status != PointPaymentStatus.Rejected && item.Status != PointPaymentStatus.Expired &&
                item.Status != PointPaymentStatus.Canceled && item.Status != PointPaymentStatus.Refunded)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return intent is null ? Results.NoContent() : Results.Ok(ToResponse(intent));
    }

    private static async Task<IResult> StartAsync(Guid? draftId, Guid? operationId,
        StartRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
        MercadoPagoQrClient qr, MercadoPagoStaticQrClient staticQr,
        MercadoPagoOrderLookupClient lookup,
        TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (request is null || request.Amount <= 0 || request.Amount > 999_999_999.99m ||
            decimal.Round(request.Amount, 2) != request.Amount ||
            request.Mode is not "point" and not "qr" and not "qrStatic" ||
            (draftId is null) == (operationId is null) ||
            (draftId is null) != (request.CustomerId is not null) ||
            request.CustomerId == Guid.Empty)
            return Error(400, "VALIDATION_ERROR");
        if (request.Mode == "point" && request.Amount < 15m)
            return Error(400, "MERCADO_PAGO_AMOUNT_TOO_SMALL");
        await using var dbTransaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        SaleDraft? draft = null;
        if (draftId is Guid saleDraftId)
        {
            draft = await OwnedDraftAsync(saleDraftId, db, accessor, cancellationToken);
            if (draft is null || draft.Lines.Count == 0 || draft.TotalAfterDiscount < request.Amount)
                return Error(409, "DRAFT_NOT_CONFIRMABLE");
        }
        else
        {
            if (await db.CustomerCollectionReceipts.AnyAsync(item =>
                item.CompanyId == accessor.Context.CompanyId && item.OperationId == operationId,
                cancellationToken)) return Error(409, "ACCOUNT_COLLECTION_ALREADY_CONFIRMED");
            if (!await db.CustomerAccounts.AnyAsync(item =>
                item.CompanyId == accessor.Context.CompanyId && item.Id == request.CustomerId,
                cancellationToken)) return Error(404, "CUSTOMER_NOT_FOUND");
        }
        if (accessor.TerminalId is not Guid terminalId)
            return Error(409, "CASHIER_SHIFT_REQUIRED");
        var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.BranchId == accessor.Context.BranchId &&
            item.CashierId == accessor.Context.UserId && item.PosTerminalId == terminalId &&
            item.Status == CashierShiftStatus.Open, cancellationToken);
        if (shift is null || draft is not null && draft.CashierShiftId != shift.Id)
            return Error(409, "CASHIER_SHIFT_REQUIRED");
        var mapping = await db.MercadoPagoRegisterSettings.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == accessor.Context.CompanyId && item.BranchId == accessor.Context.BranchId &&
            item.PosTerminalId == terminalId, cancellationToken);
        var deviceId = request.Mode == "point" ? mapping?.PointTerminalId : mapping?.QrExternalPosId;
        if (deviceId is null) return Error(409, "MERCADO_PAGO_REGISTER_NOT_CONFIGURED");
        string? token;
        try { token = await settings.AccessTokenAsync(accessor.Context.CompanyId, cancellationToken); }
        catch (CryptographicException) { return Error(409, "MERCADO_PAGO_CREDENTIAL_UNAVAILABLE"); }
        if (token is null) return Error(409, "MERCADO_PAGO_NOT_CONFIGURED");

        var mode = request.Mode switch
        {
            "qr" => MercadoPagoOrderMode.DynamicQr,
            "qrStatic" => MercadoPagoOrderMode.StaticQr,
            _ => MercadoPagoOrderMode.Point
        };
        var intent = await db.PointPaymentIntents.Where(item =>
            item.CompanyId == accessor.Context.CompanyId && item.SaleDraftId == draftId &&
            item.CustomerCollectionOperationId == operationId &&
            (item.Status == PointPaymentStatus.Prepared || item.Status == PointPaymentStatus.Pending ||
             item.Status == PointPaymentStatus.Approved || item.Status == PointPaymentStatus.NeedsReconciliation))
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (intent is not null && (intent.Amount != request.Amount || intent.Mode != mode ||
            intent.TerminalId != deviceId || intent.CustomerId != request.CustomerId ||
            intent.CashierShiftId != shift.Id || intent.CashierId != accessor.Context.UserId ||
            intent.PosTerminalId != terminalId)) return Error(409, "MERCADO_PAGO_ORDER_ACTIVE");
        if (intent?.ProviderOrderId is not null) return Results.Ok(ToResponse(intent));
        var retry = intent is not null;
        if (intent is null)
        {
            intent = draftId is Guid ownedDraftId
                ? new PointPaymentIntent(accessor.Context.CompanyId, accessor.Context.BranchId,
                    ownedDraftId, shift.Id, accessor.Context.UserId, terminalId, deviceId,
                    request.Amount, clock.GetUtcNow(), mode)
                : PointPaymentIntent.ForCollection(accessor.Context.CompanyId,
                    accessor.Context.BranchId, request.CustomerId!.Value, operationId!.Value,
                    shift.Id, accessor.Context.UserId, terminalId, deviceId,
                    request.Amount, clock.GetUtcNow(), mode);
            db.PointPaymentIntents.Add(intent);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Error(409, "MERCADO_PAGO_ORDER_ACTIVE"); }
        }
        await dbTransaction.CommitAsync(cancellationToken);

        try
        {
            if (mode == MercadoPagoOrderMode.StaticQr && intent.QrData is null)
            {
                string staticQrData;
                try { staticQrData = await staticQr.GetAsync(token, deviceId, cancellationToken); }
                catch (Exception exception) when (exception is HttpRequestException or
                    TaskCanceledException or InvalidDataException or JsonException)
                {
                    if (intent.Status == PointPaymentStatus.Prepared)
                    {
                        intent.RejectBeforeProviderOrder(clock.GetUtcNow());
                        await db.SaveChangesAsync(cancellationToken);
                        return Error(exception is InvalidDataException ? 409 : 503,
                            "MERCADO_PAGO_STATIC_QR_UNAVAILABLE");
                    }
                    throw;
                }
                intent.SetQrData(staticQrData);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (retry && await TryReconcileUnknownOrderAsync(intent, token, db,
                point, qr, lookup, clock, cancellationToken))
                return Results.Ok(ToResponse(intent));
            if (mode == MercadoPagoOrderMode.Point)
            {
                var result = await point.CreateAsync(token, new PointOrderRequest(deviceId,
                    intent.Amount, intent.ExternalReference, intent.IdempotencyKey), cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
            }
            else
            {
                var result = await qr.CreateAsync(token, new QrOrderRequest(deviceId,
                    intent.Amount, intent.ExternalReference, intent.IdempotencyKey,
                    mode == MercadoPagoOrderMode.StaticQr ? MercadoPagoQrMode.Static :
                        MercadoPagoQrMode.Dynamic), cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
                if (mode == MercadoPagoOrderMode.DynamicQr && result.QrData is not null)
                    intent.SetQrData(result.QrData);
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(intent));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            var saved = await db.PointPaymentIntents.SingleAsync(item => item.Id == intent.Id,
                cancellationToken);
            if (saved.Status is PointPaymentStatus.Prepared or PointPaymentStatus.Pending or
                PointPaymentStatus.NeedsReconciliation)
            {
                saved.MarkUncertain(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
            }
            return Error(503, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
        }
    }

    private static async Task<IResult> CheckAsync(Guid? draftId, Guid? operationId, Guid intentId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
        MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup,
        TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (draftId is Guid saleDraftId &&
            !await OwnsDraftAsync(saleDraftId, db, accessor, cancellationToken) ||
            operationId is not null && accessor.TerminalId is null)
            return Error(404, "DRAFT_NOT_FOUND");
        var intent = await db.PointPaymentIntents.SingleOrDefaultAsync(item =>
            item.Id == intentId && item.CompanyId == accessor.Context.CompanyId &&
            item.BranchId == accessor.Context.BranchId && item.SaleDraftId == draftId &&
            item.CustomerCollectionOperationId == operationId &&
            item.CashierId == accessor.Context.UserId && item.PosTerminalId == accessor.TerminalId,
            cancellationToken);
        if (intent is null) return Error(404, "PAYMENT_NOT_FOUND");
        string? token;
        try { token = await settings.AccessTokenAsync(accessor.Context.CompanyId, cancellationToken); }
        catch (CryptographicException) { return Error(409, "MERCADO_PAGO_CREDENTIAL_UNAVAILABLE"); }
        if (token is null) return Error(409, "MERCADO_PAGO_NOT_CONFIGURED");
        try
        {
            if (intent.ProviderOrderId is null)
                return await TryReconcileUnknownOrderAsync(intent, token, db, point, qr, lookup,
                    clock, cancellationToken)
                    ? Results.Ok(ToResponse(intent))
                    : Error(503, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
            if (intent.Mode == MercadoPagoOrderMode.Point)
            {
                var result = await point.GetAsync(token, intent.ProviderOrderId,
                    intent.ExternalReference, intent.Amount, cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
            }
            else
            {
                var result = await qr.GetAsync(token, intent.ProviderOrderId,
                    intent.ExternalReference, intent.Amount, cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
                if (intent.Mode == MercadoPagoOrderMode.DynamicQr && result.QrData is not null)
                    intent.SetQrData(result.QrData);
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(intent));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or InvalidOperationException)
        {
            return Error(503, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
        }
    }

    private static async Task<IResult> CancelAsync(Guid? draftId, Guid? operationId, Guid intentId,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        MercadoPagoSettingsResolver settings, MercadoPagoPointClient point,
        MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup,
        TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(context)) return Error(403, "CSRF_REJECTED");
        if (draftId is Guid saleDraftId &&
            !await OwnsDraftAsync(saleDraftId, db, accessor, cancellationToken) ||
            operationId is not null && accessor.TerminalId is null)
            return Error(404, "DRAFT_NOT_FOUND");
        var intent = await db.PointPaymentIntents.SingleOrDefaultAsync(item =>
            item.Id == intentId && item.CompanyId == accessor.Context.CompanyId &&
            item.BranchId == accessor.Context.BranchId && item.SaleDraftId == draftId &&
            item.CustomerCollectionOperationId == operationId &&
            item.CashierId == accessor.Context.UserId && item.PosTerminalId == accessor.TerminalId,
            cancellationToken);
        if (intent is null) return Error(404, "PAYMENT_NOT_FOUND");
        if (intent.Status is PointPaymentStatus.Approved or PointPaymentStatus.Refunded)
            return Error(409, "MERCADO_PAGO_ALREADY_PAID");
        if (intent.Status is PointPaymentStatus.Canceled or PointPaymentStatus.Expired or
            PointPaymentStatus.Rejected) return Results.Ok(ToResponse(intent));
        string? token;
        try { token = await settings.AccessTokenAsync(accessor.Context.CompanyId, cancellationToken); }
        catch (CryptographicException) { return Error(409, "MERCADO_PAGO_CREDENTIAL_UNAVAILABLE"); }
        if (token is null) return Error(409, "MERCADO_PAGO_NOT_CONFIGURED");
        try
        {
            if (intent.ProviderOrderId is null && !await TryReconcileUnknownOrderAsync(
                intent, token, db, point, qr, lookup, clock, cancellationToken))
                return Error(409, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
            if (intent.Status is PointPaymentStatus.Approved or PointPaymentStatus.Refunded)
                return Error(409, "MERCADO_PAGO_ALREADY_PAID");
            if (intent.Status is PointPaymentStatus.Canceled or PointPaymentStatus.Expired or
                PointPaymentStatus.Rejected) return Results.Ok(ToResponse(intent));
            var providerOrderId = intent.ProviderOrderId;
            if (providerOrderId is null)
                return Error(409, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
            if (intent.Mode == MercadoPagoOrderMode.Point)
            {
                var result = await point.CancelAsync(token, providerOrderId,
                    intent.ExternalReference, intent.Amount, intent.Id, cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
            }
            else
            {
                var result = await qr.CancelAsync(token, providerOrderId,
                    intent.ExternalReference, intent.Amount, intent.Id, cancellationToken);
                intent.RecordProviderState(result.Id, result.PaymentId, result.Status,
                    result.PaymentStatus, result.PaymentStatusDetail, result.IsApproved, clock.GetUtcNow());
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(intent));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or InvalidOperationException)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MercadoPagoPayment");
            LogCancelFailure(logger, intent.Id, exception.GetType().Name,
                (int?)(exception as HttpRequestException)?.StatusCode, context.TraceIdentifier, null);
            return Error(503, "MERCADO_PAGO_RECONCILIATION_REQUIRED");
        }
    }

    private static async Task<bool> TryReconcileUnknownOrderAsync(PointPaymentIntent intent,
        string token, PlatformAccessDbContext db, MercadoPagoPointClient point,
        MercadoPagoQrClient qr, MercadoPagoOrderLookupClient lookup, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (intent.ProviderOrderId is not null) return true;
        var type = intent.Mode == MercadoPagoOrderMode.Point ? "point" : "qr";
        var orderId = await lookup.FindIdAsync(token, intent.ExternalReference, type,
            intent.CreatedAtUtc, cancellationToken);
        if (orderId is null) return false;
        if (intent.Mode == MercadoPagoOrderMode.Point)
        {
            var order = await point.GetAsync(token, orderId, intent.ExternalReference,
                intent.Amount, cancellationToken);
            intent.RecordProviderState(order.Id, order.PaymentId, order.Status,
                order.PaymentStatus, order.PaymentStatusDetail, order.IsApproved, clock.GetUtcNow());
        }
        else
        {
            var order = await qr.GetAsync(token, orderId, intent.ExternalReference,
                intent.Amount, cancellationToken);
            intent.RecordProviderState(order.Id, order.PaymentId, order.Status,
                order.PaymentStatus, order.PaymentStatusDetail, order.IsApproved, clock.GetUtcNow());
            if (intent.Mode == MercadoPagoOrderMode.DynamicQr && order.QrData is not null)
                intent.SetQrData(order.QrData);
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Task<bool> OwnsDraftAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
        db.SaleDrafts.AnyAsync(item => item.Id == id && item.CompanyId == accessor.Context.CompanyId &&
            item.BranchId == accessor.Context.BranchId && item.UserId == accessor.Context.UserId &&
            item.PosTerminalId == accessor.TerminalId && item.Status == SaleDraftStatus.Draft,
            cancellationToken);

    private static Task<SaleDraft?> OwnedDraftAsync(Guid id, PlatformAccessDbContext db,
        OperationalContextAccessor accessor, CancellationToken cancellationToken) =>
        db.SaleDrafts.Include(item => item.Lines).SingleOrDefaultAsync(item => item.Id == id &&
            item.CompanyId == accessor.Context.CompanyId && item.BranchId == accessor.Context.BranchId &&
            item.UserId == accessor.Context.UserId && item.PosTerminalId == accessor.TerminalId &&
            item.Status == SaleDraftStatus.Draft, cancellationToken);

    private static object ToResponse(PointPaymentIntent intent) => new
    {
        id = intent.Id, mode = intent.Mode switch
        {
            MercadoPagoOrderMode.Point => "point",
            MercadoPagoOrderMode.StaticQr => "qrStatic",
            _ => "qr"
        },
        amount = intent.Amount, status = intent.Status.ToString(),
        providerOrderId = intent.ProviderOrderId, qrData = intent.QrData,
        approved = intent.Status == PointPaymentStatus.Approved,
        createdAtUtc = intent.CreatedAtUtc
    };

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(new
    {
        error = new { code, message = "No se pudo completar el cobro de Mercado Pago" }
    }, statusCode: status);

    private sealed record StartRequest(string Mode, decimal Amount, Guid? CustomerId = null);
}
