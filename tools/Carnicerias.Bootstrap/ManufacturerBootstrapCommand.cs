using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Bootstrap;

internal static class ManufacturerBootstrapCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5 || args[0] != "create-superadmin" ||
            args[1] != "--username" || args[3] != "--email" ||
            string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(args[4]))
        {
            Console.Error.WriteLine("Uso: create-superadmin --username <usuario> --email <correo>. La contraseña se ingresa en consola.");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Configura CARNICERIAS_CONNECTION_STRING antes de continuar.");
            return 2;
        }

        try
        {
            var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var db = new PlatformAccessDbContext(options);
            if ((await db.Database.GetPendingMigrationsAsync()).Any())
            {
                Console.Error.WriteLine("Aplicá primero las migraciones controladas a la única base autorizada.");
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

            if (password != confirmation ||
                !VisualDevelopmentPasswordPolicy.IsValid(password, connectionString))
            {
                Console.Error.WriteLine("Las contraseñas no coinciden o no cumplen la política de esta instalación.");
                return 2;
            }

            var bootstrapper = new BootstrapManufacturerService(db, new Argon2idPasswordHasher());
            await bootstrapper.CreateFirstSuperadminAsync(args[2], args[4], password);
            Console.WriteLine("Superadmin del fabricante creado.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo crear el superadmin. No se imprimieron credenciales.");
            return 1;
        }
    }
}
