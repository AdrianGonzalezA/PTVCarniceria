using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class PlatformAccessDatabaseTests
{
    [PostgreSqlFact]
    [Trait("Category", "Integration")]
    public async Task MigrationEnforcesIdentityAndScopeConstraintsAndRollsBackCleanly()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING");
        var connection = new NpgsqlConnectionStringBuilder(connectionString!);
        if (connection.Database?.StartsWith("carnicerias_test_", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                "The integration database name must start with 'carnicerias_test_' to prevent data loss.");
        }

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using (var db = new PlatformAccessDbContext(options))
        {
            var existingSchema = await db.Database.SqlQueryRaw<string>(
                "SELECT schema_name AS \"Value\" FROM information_schema.schemata WHERE schema_name = 'platform_access'")
                .FirstOrDefaultAsync();
            Assert.Null(existingSchema);

            await db.Database.MigrateAsync();
            Assert.Equal(8, await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'platform_access'")
                .SingleAsync());
        }

        await using (var db = new PlatformAccessDbContext(options))
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var firstCompany = new Company("Empresa Uno");
            var otherCompany = new Company("Empresa Dos");
            var role = new Role("clerk", "Carnicero");
            var firstBranch = new Branch(firstCompany.Id, "Centro");
            var user = UserIdentity.Create("admin", "admin@example.test", "test-only-hash");
            db.AddRange(firstCompany, otherCompany, role, firstBranch, user);
            await db.SaveChangesAsync();

            var duplicate = UserIdentity.Create(" ADMIN ", "other@example.test", "test-only-hash");
            db.Users.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(duplicate).State = EntityState.Detached;

            var duplicateEmail = UserIdentity.Create("different-user", " ADMIN@example.test ", "test-only-hash");
            db.Users.Add(duplicateEmail);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(duplicateEmail).State = EntityState.Detached;

            db.UserAssignments.Add(new UserAssignment(
                user.Id,
                role.Id,
                otherCompany.Id,
                firstBranch.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await transaction.RollbackAsync();
        }

        var initialPassword = "correct horse battery staple";
        var initialPermissions = PlatformPermissionCatalog.CreateDefaultPermissions();
        Guid administratorId;
        await using (var db = new PlatformAccessDbContext(options))
        {
            var bootstrapper = new BootstrapAdminService(db, new Argon2idPasswordHasher());
            administratorId = await bootstrapper.CreateFirstAdministratorAsync(
                "installer-admin",
                "installer-admin@example.test",
                "Carnicerías de prueba",
                "Casa central",
                initialPassword,
                initialPermissions);
        }

        await using (var db = new PlatformAccessDbContext(options))
        {
            var administrator = await db.Users.SingleAsync(user => user.Id == administratorId);
            Assert.True(new Argon2idPasswordHasher().Verify(initialPassword, administrator.PasswordHash));
            Assert.DoesNotContain(initialPassword, administrator.PasswordHash, StringComparison.Ordinal);
            Assert.Equal("administrator", await db.Roles.Select(role => role.Code).SingleAsync());
            Assert.Equal(1, await db.UserAssignments.CountAsync(assignment =>
                assignment.UserId == administratorId && assignment.BranchId == null));
            Assert.Equal(4, await db.RolePermissions.CountAsync());
            Assert.Equal(
                ["inventory.stock.manage", "platform.assignments.manage", "platform.roles.manage", "platform.users.manage"],
                await db.RolePermissions
                    .Join(db.Permissions, capability => capability.PermissionId, permission => permission.Id,
                        (capability, permission) => permission.Code)
                    .OrderBy(code => code)
                    .ToListAsync());

            var bootstrapper = new BootstrapAdminService(db, new Argon2idPasswordHasher());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bootstrapper.CreateFirstAdministratorAsync(
                    "another-admin",
                    "another-admin@example.test",
                    "Otra empresa",
                    "Otra sucursal",
                    initialPassword,
                    []));
        }

        await using (var db = new PlatformAccessDbContext(options))
        {
            await db.Database.MigrateAsync("0");
            var remainingTables = await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'platform_access'")
                .SingleAsync();
            Assert.Equal(0, remainingTables);

            Assert.Empty(await db.Database.GetAppliedMigrationsAsync());

            var remainingSchema = await db.Database.SqlQueryRaw<string>(
                "SELECT schema_name AS \"Value\" FROM information_schema.schemata WHERE schema_name = 'platform_access'")
                .FirstOrDefaultAsync();
            Assert.Null(remainingSchema);
        }
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")))
        {
            Skip = "Set CARNICERIAS_TEST_CONNECTION_STRING to an empty, disposable PostgreSQL test database.";
        }
    }
}
