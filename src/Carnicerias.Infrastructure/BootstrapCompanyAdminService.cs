using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed class BootstrapCompanyAdminService(PlatformAccessDbContext db, IPasswordHasher passwordHasher)
{
    public async Task<Guid> CreateMissingAdministratorAsync(
        Guid companyId, string username, string email, string password,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1128352846, 1112493908);", cancellationToken);

        if (!await db.Companies.AnyAsync(company => company.Id == companyId, cancellationToken))
            throw new InvalidOperationException("La empresa indicada no existe.");

        var adminRoleId = await db.Roles.Where(role => role.Code == "administrator")
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(cancellationToken);
        if (adminRoleId is null)
            throw new InvalidOperationException("El rol administrador no existe.");

        if (await db.UserAssignments.AnyAsync(assignment => assignment.CompanyId == companyId &&
            assignment.RoleId == adminRoleId.Value, cancellationToken))
            throw new InvalidOperationException("La empresa ya tiene administrador; no se creó otro por bootstrap.");

        var user = UserIdentity.CreateForCompany(companyId, username, email, passwordHasher.Hash(password));
        db.Users.Add(user);
        db.UserAssignments.Add(new UserAssignment(user.Id, adminRoleId.Value, companyId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }
}
