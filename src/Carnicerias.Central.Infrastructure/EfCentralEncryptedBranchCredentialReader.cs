using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Infrastructure;

public sealed class EfCentralEncryptedBranchCredentialReader(
    IDbContextFactory<CentralAccessDbContext> contextFactory)
    : ICentralEncryptedBranchCredentialReader
{
    public async Task<CentralEncryptedBranchCredential?> FindAsync(Guid installationId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var credential = await context.BranchCredentials.AsNoTracking()
            .SingleOrDefaultAsync(item => item.InstallationId == installationId,
                cancellationToken);
        return credential?.ToEnvelope();
    }
}
