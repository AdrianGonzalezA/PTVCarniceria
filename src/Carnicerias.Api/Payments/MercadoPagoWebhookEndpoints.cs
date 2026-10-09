using System.Security.Cryptography;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public static class MercadoPagoWebhookEndpoints
{
    public static IEndpointRouteBuilder MapMercadoPagoWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/payments/mercado-pago/webhook", ReceiveAsync);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(HttpRequest request,
        PlatformAccessDbContext db, MercadoPagoSettingsResolver settings,
        MercadoPagoPointClient point, MercadoPagoQrClient qr, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var orderId = request.Query["data.id"].ToString();
        if (request.Query["type"] != "order" || string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 100 || !orderId.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            return Results.BadRequest();
        var intent = await db.PointPaymentIntents.SingleOrDefaultAsync(item =>
            item.ProviderOrderId == orderId, cancellationToken);
        if (intent is null) return Results.Ok();
        string? secret;
        try { secret = await settings.WebhookSecretAsync(intent.CompanyId, cancellationToken); }
        catch (CryptographicException) { return Results.StatusCode(503); }
        if (!MercadoPagoWebhookSignature.IsValid(request.Headers["x-signature"],
            request.Headers["x-request-id"], orderId, secret)) return Results.Unauthorized();
        string? token;
        try { token = await settings.AccessTokenAsync(intent.CompanyId, cancellationToken); }
        catch (CryptographicException) { return Results.StatusCode(503); }
        if (token is null) return Results.StatusCode(503);
        try
        {
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
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or InvalidOperationException)
        { return Results.StatusCode(503); }
    }
}
