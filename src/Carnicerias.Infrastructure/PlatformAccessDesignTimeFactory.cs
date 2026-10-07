using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Carnicerias.Infrastructure;

public sealed class PlatformAccessDesignTimeFactory : IDesignTimeDbContextFactory<PlatformAccessDbContext>
{
    public PlatformAccessDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CONNECTION_STRING")
            ?? "Host=localhost;Database=carnicerias_design;Username=postgres";

        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PlatformAccessDbContext(options);
    }
}
