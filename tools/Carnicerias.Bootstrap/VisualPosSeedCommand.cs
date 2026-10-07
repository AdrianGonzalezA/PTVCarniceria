using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Bootstrap;

internal static class VisualPosSeedCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["seed-pos-visual"])
        {
            Console.Error.WriteLine("Usage: Carnicerias.Bootstrap seed-pos-visual");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING");
        var adminPassword = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_ADMIN_PASSWORD") ?? "";
        var cashierPassword = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_CASHIER_PASSWORD") ?? "";
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !BootstrapPasswordPolicy.IsValid(adminPassword) ||
            !BootstrapPasswordPolicy.IsValid(cashierPassword))
        {
            Console.Error.WriteLine("Configure la base visual y las dos contraseñas de prueba en variables de entorno.");
            return 2;
        }

        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Database != "carnicerias_test_visual" ||
            connection.Host != "127.0.0.1" || connection.Port != 55433)
        {
            Console.Error.WriteLine("Este comando solo admite la base visual descartable local.");
            return 2;
        }

        try
        {
            var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var db = new PlatformAccessDbContext(options);
            await db.Database.MigrateAsync();
            if (await db.Users.AnyAsync())
            {
                Console.Error.WriteLine("La base visual ya contiene usuarios; no se modificó nada.");
                return 2;
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var hasher = new Argon2idPasswordHasher();
            await new BootstrapAdminService(db, hasher).CreateFirstAdministratorAsync(
                "visual-admin", "visual-admin@example.test", "Empresa Visual", "Sucursal Visual",
                adminPassword, PlatformPermissionCatalog.CreateDefaultPermissions());
            var company = await db.Companies.SingleAsync();
            var branch = await db.Branches.SingleAsync();
            var cashier = UserIdentity.Create("visual-cashier", "visual-cashier@example.test",
                hasher.Hash(cashierPassword));
            var role = new Role("cashier", "Cajero");
            db.AddRange(cashier, role, new UserAssignment(cashier.Id, role.Id, company.Id, branch.Id));
            await db.SaveChangesAsync();

            var terminals = new PosTerminalProvisioningService(db);
            var first = await terminals.CreateAsync(branch.Id, "Caja 1");
            var second = await terminals.CreateAsync(branch.Id, "Caja 2");
            await transaction.CommitAsync();
            Console.WriteLine("Usuarios de prueba: visual-admin y visual-cashier.");
            Console.WriteLine($"Sucursal: {branch.Id}");
            Console.WriteLine($"Caja 1: {first.Terminal.Id} | credencial: {first.Credential.Token}");
            Console.WriteLine($"Caja 2: {second.Terminal.Id} | credencial: {second.Credential.Token}");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo inicializar la base visual descartable.");
            return 1;
        }
    }
}
