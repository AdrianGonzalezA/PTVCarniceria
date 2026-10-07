using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed class PosTerminalAuthenticationService(PlatformAccessDbContext db)
{
    public async Task<PosTerminal?> FindActiveAsync(
        string? credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length != 43)
            return null;

        string hash;
        try
        {
            hash = PosTerminalCredential.ComputeHash(credential);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return await db.PosTerminals.AsNoTracking().SingleOrDefaultAsync(
            terminal => terminal.CredentialHash == hash && terminal.IsActive && !terminal.IsHistorical,
            cancellationToken);
    }
}
