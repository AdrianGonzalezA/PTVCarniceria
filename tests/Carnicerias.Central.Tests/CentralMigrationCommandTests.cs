using Carnicerias.Central.Bootstrap;

namespace Carnicerias.Central.Tests;

public sealed class CentralMigrationCommandTests
{
    [Fact]
    public async Task RejectsWrongDatabaseBeforeOpeningConnection()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralMigrationCommand.RunAsync(
                "Host=127.0.0.1;Port=55433;Database=postgres;Username=postgres",
                "missing.dump",
                new string('0', 64),
                CancellationToken.None));

        Assert.Contains("visual database", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsMissingBackupBeforeOpeningConnection()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CentralMigrationCommand.RunAsync(
                "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres",
                "missing.dump",
                new string('0', 64),
                CancellationToken.None));

        Assert.Contains("backup", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
