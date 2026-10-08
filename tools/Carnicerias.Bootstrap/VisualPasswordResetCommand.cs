using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Bootstrap;

public static class VisualPasswordResetCommand
{
    public static bool IsAllowedTarget(string? connectionString) =>
        VisualDevelopmentPasswordPolicy.IsVisualDatabase(connectionString);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["reset-visual-passwords"])
        {
            Console.Error.WriteLine("Usage: Carnicerias.Bootstrap reset-visual-passwords");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING");
        var password = Environment.GetEnvironmentVariable("CARNICERIAS_VISUAL_PASSWORD");
        if (password is null || !IsAllowedTarget(connectionString) ||
            !VisualDevelopmentPasswordPolicy.IsValid(password, connectionString))
        {
            Console.Error.WriteLine("Configure la base visual local y CARNICERIAS_VISUAL_PASSWORD.");
            return 2;
        }

        try
        {
            var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
                .UseNpgsql(connectionString).Options;
            await using var db = new PlatformAccessDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var users = await db.Users.ToArrayAsync();
            if (users.Length == 0)
            {
                Console.Error.WriteLine("La base visual no contiene usuarios; no se modificó nada.");
                return 2;
            }

            var hasher = new Argon2idPasswordHasher();
            foreach (var user in users) user.SetPasswordHash(hasher.Hash(password));

            var now = DateTimeOffset.UtcNow;
            var sessions = await db.Sessions.Where(session => session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now).ToArrayAsync();
            foreach (var session in sessions) session.Revoke(now);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine($"Contraseñas actualizadas: {users.Length}. Sesiones revocadas: {sessions.Length}.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("No se pudo actualizar la base visual. No se muestran credenciales.");
            return 1;
        }
    }
}
