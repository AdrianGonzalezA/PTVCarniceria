using System.Text;
using Carnicerias.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Payments;

public sealed class MercadoPagoSettingsResolver(PlatformAccessDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Carnicerias.MercadoPago.Credentials.v1");

    public byte[] Protect(string value) => _protector.Protect(Encoding.UTF8.GetBytes(value));

    public string Unprotect(byte[] value) => Encoding.UTF8.GetString(_protector.Unprotect(value));

    public async Task<string?> AccessTokenAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoGatewaySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        return settings?.ProtectedAccessToken is { Length: > 0 } encrypted
            ? Unprotect(encrypted) : null;
    }

    public async Task<string?> WebhookSecretAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoGatewaySettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        return settings?.ProtectedWebhookSecret is { Length: > 0 } encrypted
            ? Unprotect(encrypted) : null;
    }
}
