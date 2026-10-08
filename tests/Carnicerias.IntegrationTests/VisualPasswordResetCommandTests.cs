using Carnicerias.Bootstrap;

namespace Carnicerias.IntegrationTests;

public sealed class VisualPasswordResetCommandTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres", true)]
    [InlineData("Host=localhost;Port=55433;Database=carnicerias_test_visual;Username=postgres", false)]
    [InlineData("Host=127.0.0.1;Port=5432;Database=carnicerias_test_visual;Username=postgres", false)]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_production;Username=postgres", false)]
    public void OnlyTheExistingLocalVisualDatabaseIsAllowed(string connectionString, bool expected)
    {
        Assert.Equal(expected, VisualPasswordResetCommand.IsAllowedTarget(connectionString));
    }
}
