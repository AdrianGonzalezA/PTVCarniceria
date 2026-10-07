using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed record ProvisionedPosTerminal(PosTerminal Terminal, PosTerminalCredential Credential);

public sealed class PosTerminalProvisioningService(PlatformAccessDbContext db)
{
    public async Task<ProvisionedPosTerminal> CreateAsync(
        Guid branchId, string name, CancellationToken cancellationToken = default)
    {
        if (branchId == Guid.Empty || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A branch and terminal name are required.");

        var branch = await db.Branches.SingleOrDefaultAsync(
            candidate => candidate.Id == branchId && candidate.IsActive, cancellationToken);
        if (branch is null || !await db.Companies.AnyAsync(
                company => company.Id == branch.CompanyId && company.IsActive, cancellationToken))
            throw new InvalidOperationException("The selected branch is unavailable.");

        var normalizedName = name.Trim();
        if (await db.PosTerminals.AnyAsync(
                terminal => terminal.BranchId == branchId && terminal.Name == normalizedName,
                cancellationToken))
            throw new InvalidOperationException("A terminal with that name already exists in the branch.");

        var credential = PosTerminalCredential.Issue();
        var terminal = new PosTerminal(branch.CompanyId, branch.Id, normalizedName);
        terminal.AssignCredentialHash(credential.Hash);
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync(cancellationToken);
        return new ProvisionedPosTerminal(terminal, credential);
    }
}
