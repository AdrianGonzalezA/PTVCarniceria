using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Bootstrap;

internal static class BootstrapCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        BootstrapAdminArguments arguments;
        try
        {
            arguments = BootstrapArgumentParser.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Configura CARNICERIAS_CONNECTION_STRING antes de ejecutar el bootstrap.");
            return 2;
        }

        try
        {
            var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var db = new PlatformAccessDbContext(options);
            await db.Database.MigrateAsync();

            if (await db.Users.AnyAsync())
            {
                Console.Error.WriteLine(
                    "La instalación ya contiene usuarios; el administrador inicial no se creó.");
                return 2;
            }

            string password;
            string confirmation;
            try
            {
                password = HiddenPasswordPrompt.Read("Contraseña nueva: ");
                confirmation = HiddenPasswordPrompt.Read("Repite la contraseña: ");
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine("La contraseña debe ingresarse en una consola interactiva.");
                return 2;
            }

            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Las contraseñas no coinciden. No se realizaron cambios.");
                return 2;
            }

            if (!BootstrapPasswordPolicy.IsValid(password))
            {
                Console.Error.WriteLine("La contraseña debe tener al menos 12 caracteres y no superar el límite admitido.");
                return 2;
            }

            var bootstrapper = new BootstrapAdminService(db, new Argon2idPasswordHasher());
            await bootstrapper.CreateFirstAdministratorAsync(
                arguments.Username,
                arguments.Email,
                arguments.CompanyName,
                arguments.BranchName,
                password,
                PlatformPermissionCatalog.CreateDefaultPermissions());

            Console.WriteLine("Administrador inicial creado correctamente.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo completar la inicialización. No se registraron credenciales.");
            return 1;
        }
    }
}
