using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Fiscal;

public static class AdminArcaSettingsEndpoints
{
    private const int MaxUploadBodyBytes = 1_500_000;

    public static IEndpointRouteBuilder MapAdminArcaSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/arca-settings")
            .RequireOperationalPermission(PlatformPermissionCatalog.OrganizationManage);
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
        group.MapPost("/certificate", UploadCertificateAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        OperationalContextAccessor accessor, ArcaSettingsResolver resolver,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var companyId = accessor.Context.CompanyId;
        var saved = await db.ArcaCompanySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        if (saved is not null)
        {
            var certificateAccessible = false;
            if (saved.ProtectedPfx is not null)
            {
                try
                {
                    var runtime = await resolver.ResolveAsync(companyId, cancellationToken);
                    using var loadedCertificate = runtime.LoadCertificate();
                    certificateAccessible = loadedCertificate.HasPrivateKey;
                }
                catch (Exception exception) when (exception is CryptographicException or IOException)
                { /* Show metadata but clearly flag an unavailable key. */ }
            }
            return Results.Ok(ToResponse(saved, timeProvider.GetUtcNow(), certificateAccessible));
        }
        var fallback = await resolver.ResolveAsync(companyId, cancellationToken);
        ArcaCertificateDetails? certificate = null;
        if (fallback.PfxPath is not null && File.Exists(fallback.PfxPath))
        {
            try { certificate = ArcaCertificateInspector.Inspect(
                await File.ReadAllBytesAsync(fallback.PfxPath, cancellationToken),
                fallback.Password, timeProvider.GetUtcNow()); }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
        return Results.Ok(new
        {
            companyId, source = fallback.IsConfigured ? "environment" : "none",
            issuerCuit = fallback.IssuerCuit, pointOfSale = fallback.PointOfSale,
            issuerName = fallback.IssuerName, issuerAddress = fallback.IssuerAddress,
            issuerIibb = fallback.IssuerIibb,
            issuerActivityStartDate = fallback.IssuerActivityStartDate,
            certificate = CertificateResponse(certificate, timeProvider.GetUtcNow(), certificate is not null)
        });
    }

    private static async Task<IResult> UpdateAsync(ArcaMetadataRequest? request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        TimeProvider timeProvider, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(httpContext)) return Error(403, "CSRF_REJECTED");
        if (request is null) return Error(400, "VALIDATION_ERROR");
        DateOnly? activityStartDate = null;
        if (!string.IsNullOrWhiteSpace(request.IssuerActivityStartDate))
        {
            if (!DateOnly.TryParseExact(request.IssuerActivityStartDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
                return Error(400, "VALIDATION_ERROR");
            activityStartDate = date;
        }
        var companyId = accessor.Context.CompanyId;
        var settings = await db.ArcaCompanySettings.SingleOrDefaultAsync(item => item.CompanyId == companyId,
            cancellationToken);
        try
        {
            if (settings is null)
            {
                settings = new ArcaCompanySettings(companyId, request.IssuerCuit,
                    request.PointOfSale, request.IssuerName, request.IssuerAddress,
                    request.IssuerIibb, activityStartDate, accessor.Context.UserId,
                    timeProvider.GetUtcNow());
                db.ArcaCompanySettings.Add(settings);
            }
            else settings.Update(request.IssuerCuit, request.PointOfSale, request.IssuerName,
                request.IssuerAddress, request.IssuerIibb, activityStartDate,
                accessor.Context.UserId, timeProvider.GetUtcNow());
        }
        catch (ArgumentException) { return Error(400, "VALIDATION_ERROR"); }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(settings, timeProvider.GetUtcNow(), settings.ProtectedPfx is not null));
    }

    private static async Task<IResult> UploadCertificateAsync(HttpRequest request,
        PlatformAccessDbContext db, OperationalContextAccessor accessor,
        ArcaSettingsResolver resolver, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!AllowedOrigin(request.HttpContext)) return Error(403, "CSRF_REJECTED");
        if (request.ContentLength is > MaxUploadBodyBytes) return Error(413, "PFX_TOO_LARGE");
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = MaxUploadBodyBytes;
        var companyId = accessor.Context.CompanyId;
        var settings = await db.ArcaCompanySettings.SingleOrDefaultAsync(item => item.CompanyId == companyId,
            cancellationToken);
        if (settings is null) return Error(409, "ARCA_SETTINGS_REQUIRED");
        ArcaCertificateUploadRequest? upload;
        try { upload = await request.ReadFromJsonAsync<ArcaCertificateUploadRequest>(
            cancellationToken: cancellationToken); }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException)
        { return Error(400, "PFX_INVALID"); }
        if (upload?.ContentBase64 is null || upload.ContentBase64.Length > 1_400_000)
            return Error(413, "PFX_TOO_LARGE");
        byte[] pfx;
        ArcaCertificateDetails details;
        try
        {
            pfx = Convert.FromBase64String(upload.ContentBase64);
            details = ArcaCertificateInspector.Inspect(pfx, upload.Password, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or CryptographicException)
        { return Error(400, "PFX_INVALID"); }
        settings.AssignCertificate(resolver.Protect(pfx), string.IsNullOrEmpty(upload.Password) ? null :
            resolver.Protect(Encoding.UTF8.GetBytes(upload.Password)), details.Subject,
            details.Thumbprint, details.NotBeforeUtc, details.NotAfterUtc,
            accessor.Context.UserId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(settings, timeProvider.GetUtcNow(), true));
    }

    private static object ToResponse(ArcaCompanySettings settings, DateTimeOffset now,
        bool certificateAccessible) => new
    {
        companyId = settings.CompanyId, source = "database", issuerCuit = settings.IssuerCuit,
        pointOfSale = settings.PointOfSale, issuerName = settings.IssuerName,
        issuerAddress = settings.IssuerAddress, issuerIibb = settings.IssuerIibb,
        issuerActivityStartDate = settings.IssuerActivityStartDate?.ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture),
        certificate = CertificateResponse(settings.CertificateSubject is null ? null :
            new ArcaCertificateDetails(settings.CertificateSubject, settings.CertificateThumbprint!,
                settings.CertificateNotBeforeUtc!.Value, settings.CertificateNotAfterUtc!.Value), now,
                certificateAccessible)
    };

    private static object? CertificateResponse(ArcaCertificateDetails? certificate, DateTimeOffset now,
        bool isAccessible) =>
        certificate is null ? null : new
        {
            subject = certificate.Subject, thumbprint = certificate.Thumbprint,
            validFromUtc = certificate.NotBeforeUtc, expiresAtUtc = certificate.NotAfterUtc,
            isExpired = certificate.NotAfterUtc <= now, isAccessible
        };

    private static bool AllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || RequestOriginValidator.IsAllowed(origin, context.Request);
    }

    private static IResult Error(int status, string code) => Results.Json(new
    {
        error = new { code, message = "No se pudo guardar la configuración de ARCA" }
    }, statusCode: status);

    private sealed record ArcaMetadataRequest(string IssuerCuit, int PointOfSale,
        string IssuerName, string IssuerAddress, string? IssuerIibb,
        string? IssuerActivityStartDate);
    private sealed record ArcaCertificateUploadRequest(string ContentBase64, string? Password);
}
