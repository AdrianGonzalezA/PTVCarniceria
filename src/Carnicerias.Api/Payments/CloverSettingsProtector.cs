using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Carnicerias.Api.Payments;

public sealed class CloverSettingsProtector(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Carnicerias.Clover.Credentials.v1");

    public byte[] Protect(string value) => _protector.Protect(Encoding.UTF8.GetBytes(value));

    public string Unprotect(byte[] value) => Encoding.UTF8.GetString(_protector.Unprotect(value));
}
