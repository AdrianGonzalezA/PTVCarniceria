using Carnicerias.Domain.PlatformAccess;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed record OperationalBranchOption(Guid BranchId, string BranchName);

public sealed record OperationalCompanyOption(
    Guid CompanyId,
    string CompanyName,
    IReadOnlyList<OperationalBranchOption> Branches);

public sealed record AuthorizedOperationalContext(
    OperationalContext Context,
    string CompanyName,
    string BranchName);

public sealed class OperationalContextAccessService(PlatformAccessDbContext db)
{
    public async Task<IReadOnlyList<OperationalCompanyOption>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var assignments = await db.UserAssignments
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => new { assignment.CompanyId, assignment.BranchId })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            return [];
        }

        var companyIds = assignments.Select(assignment => assignment.CompanyId).Distinct().ToArray();
        var companies = await db.Companies
            .Where(company => company.IsActive && companyIds.Contains(company.Id))
            .Select(company => new { company.Id, company.Name })
            .ToDictionaryAsync(company => company.Id, company => company.Name, cancellationToken);
        var branches = await db.Branches
            .Where(branch => branch.IsActive && companyIds.Contains(branch.CompanyId))
            .Select(branch => new { branch.Id, branch.CompanyId, branch.Name })
            .ToListAsync(cancellationToken);

        return companies
            .Select(company => new OperationalCompanyOption(
                company.Key,
                company.Value,
                branches
                    .Where(branch => branch.CompanyId == company.Key && assignments.Any(assignment =>
                        assignment.CompanyId == company.Key &&
                        (assignment.BranchId is null || assignment.BranchId == branch.Id)))
                    .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(branch => new OperationalBranchOption(branch.Id, branch.Name))
                    .ToArray()))
            .Where(company => company.Branches.Count > 0)
            .OrderBy(company => company.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AuthorizedOperationalContext?> ResolveAsync(
        Guid userId,
        Guid sessionId,
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies
            .Where(candidate => candidate.Id == companyId && candidate.IsActive)
            .Select(candidate => candidate.Name)
            .SingleOrDefaultAsync(cancellationToken);
        var branch = await db.Branches
            .Where(candidate => candidate.Id == branchId && candidate.CompanyId == companyId && candidate.IsActive)
            .Select(candidate => candidate.Name)
            .SingleOrDefaultAsync(cancellationToken);

        if (company is null || branch is null)
        {
            return null;
        }

        var assigned = await db.UserAssignments.AnyAsync(
            assignment => assignment.UserId == userId &&
                          assignment.CompanyId == companyId &&
                          (assignment.BranchId == null || assignment.BranchId == branchId),
            cancellationToken);
        if (!assigned)
        {
            return null;
        }

        var roleIds = await db.UserAssignments
            .Where(assignment => assignment.UserId == userId &&
                                 assignment.CompanyId == companyId &&
                                 (assignment.BranchId == null || assignment.BranchId == branchId))
            .Select(assignment => assignment.RoleId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var permissions = await (
                from rolePermission in db.RolePermissions
                join permission in db.Permissions on rolePermission.PermissionId equals permission.Id
                where roleIds.Contains(rolePermission.RoleId)
                select permission.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var context = new OperationalContext(
            userId,
            companyId,
            branchId,
            permissions.ToHashSet(StringComparer.Ordinal),
            sessionId);
        return new AuthorizedOperationalContext(context, company, branch);
    }

    public async Task<AuthorizedOperationalContext?> ResolveSessionAsync(
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        if (session.CompanyId is not Guid companyId || session.BranchId is not Guid branchId)
        {
            return null;
        }

        var context = await ResolveAsync(
            session.UserId,
            session.Id,
            companyId,
            branchId,
            cancellationToken);
        if (context is null)
        {
            session.ClearOperationalContext();
            await db.SaveChangesAsync(cancellationToken);
        }

        return context;
    }

    public async Task<AuthorizedOperationalContext?> SelectAsync(
        UserSession session,
        Guid companyId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAsync(
            session.UserId,
            session.Id,
            companyId,
            branchId,
            cancellationToken);
        if (context is null)
        {
            return null;
        }

        session.SelectOperationalContext(companyId, branchId);
        await db.SaveChangesAsync(cancellationToken);
        return context;
    }
}
