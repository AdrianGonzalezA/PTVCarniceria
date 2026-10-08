using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class VisualDevelopmentPasswordPolicyTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual", 6)]
    [InlineData("Host=127.0.0.1;Port=5432;Database=carnicerias_test_visual", 12)]
    [InlineData("Host=127.0.0.1;Port=55433;Database=carnicerias_prod", 12)]
    [InlineData("Host=localhost;Port=55433;Database=carnicerias_test_visual", 12)]
    [InlineData("Host=127.0.0.1;Port=invalid;Database=carnicerias_test_visual", 12)]
    public void MinimumLengthIsShortOnlyForTheVisualDatabase(string connectionString, int expected)
    {
        Assert.Equal(expected, VisualDevelopmentPasswordPolicy.MinimumLength(connectionString));
    }

    [Fact]
    public void ShortPasswordIsOnlyAcceptedOnTheVisualDatabase()
    {
        const string visual = "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual";
        const string other = "Host=127.0.0.1;Port=55433;Database=carnicerias_prod";
        Assert.True(VisualDevelopmentPasswordPolicy.IsValid("abcdef", visual));
        Assert.False(VisualDevelopmentPasswordPolicy.IsValid("abcdef", other));
        Assert.False(VisualDevelopmentPasswordPolicy.IsValid("12345", visual));
    }
}
