using Carnicerias.Central.Bootstrap;

namespace Carnicerias.Central.Tests;

public sealed class CentralConnectionGuardTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")]
    [InlineData("Server=127.0.0.1;Port=55433;Database=carnicerias_test_visual;User ID=postgres;Password=example")]
    public void AcceptsOnlyTheExistingVisualDatabase(string connectionString)
    {
        CentralConnectionGuard.Validate(connectionString);
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=55433;Database=postgres;Username=postgres")]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_clone;Username=postgres")]
    [InlineData("Host=localhost;Port=55433;Database=carnicerias_test_visual;Username=postgres")]
    [InlineData("Host=127.0.0.1,10.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=carnicerias_test_visual;Username=postgres")]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres;Search Path=public")]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres;Options=-c search_path=public")]
    public void RejectsAnyOtherTargetOrSchemaOverride(string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CentralConnectionGuard.Validate(connectionString));
        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingConnectionWithoutLeakingConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => CentralConnectionGuard.Validate(null));
    }
}
