using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Infrastructure;

public sealed class BootstrapAdminService(
    PlatformAccessDbContext db,
    IPasswordHasher passwordHasher)
{
    public async Task<Guid> CreateFirstAdministratorAsync(
        string username,
        string email,
        string companyName,
        string branchName,
        string password,
        IReadOnlyCollection<PermissionDefinition> initialPermissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialPermissions);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1128352846, 1112493908);",
            cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "La instalación ya contiene usuarios; el administrador inicial no se creó.");
        }

        var company = new Company(companyName);
        var branch = new Branch(company.Id, branchName);
        var role = new Role("administrator", "Administrador");
        var user = UserIdentity.Create(username, email, passwordHasher.Hash(password));
        var existingPermissions = await db.Permissions.ToDictionaryAsync(
            permission => permission.Code, cancellationToken);
        var permissions = initialPermissions.Select(permission =>
            existingPermissions.GetValueOrDefault(permission.Code) ?? permission).ToArray();

        db.AddRange(company, branch, role, user);
        db.Permissions.AddRange(permissions.Where(permission => !existingPermissions.ContainsKey(permission.Code)));
        db.RolePermissions.AddRange(permissions.Select(permission =>
            new RoleCapability(role.Id, permission.Id)));
        db.UserAssignments.Add(new UserAssignment(user.Id, role.Id, company.Id));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw new InvalidOperationException(
                "La instalación ya contiene datos incompatibles; el administrador inicial no se creó.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
}
