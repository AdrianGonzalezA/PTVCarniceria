using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Bootstrap;

internal static class TerminalProvisionCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        TerminalProvisionArguments? arguments = null;
        var listBranches = args is ["list-branches"];
        try
        {
            if (!listBranches) arguments = TerminalProvisionArgumentParser.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Configura CARNICERIAS_CONNECTION_STRING antes de provisionar una caja.");
            return 2;
        }

        try
        {
            var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var db = new PlatformAccessDbContext(options);
            if (listBranches)
            {
                var branches = await db.Branches.AsNoTracking()
                    .Where(branch => branch.IsActive)
                    .OrderBy(branch => branch.Name)
                    .Select(branch => new { branch.Id, branch.CompanyId, branch.Name })
                    .ToArrayAsync();
                foreach (var branch in branches)
                    Console.WriteLine($"{branch.Id} | {branch.Name} | empresa {branch.CompanyId}");
                return 0;
            }

            await db.Database.MigrateAsync();
            var created = await new PosTerminalProvisioningService(db)
                .CreateAsync(arguments!.BranchId, arguments.Name);
            Console.WriteLine($"Caja creada: {created.Terminal.Name} ({created.Terminal.Id}).");
            Console.WriteLine($"Credencial de un solo uso para configurar Electron: {created.Credential.Token}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo provisionar la caja; verifica la conexion y el estado de la sucursal.");
            return 1;
        }
    }
}
