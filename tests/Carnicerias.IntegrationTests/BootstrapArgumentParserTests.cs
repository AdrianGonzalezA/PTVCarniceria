using Carnicerias.Bootstrap;

namespace Carnicerias.IntegrationTests;

public sealed class BootstrapArgumentParserTests
{
    [Fact]
    public void ParseUsesSafeInitialContextDefaults()
    {
        var parsed = BootstrapArgumentParser.Parse(
            ["create-admin", "--username", "admin", "--email", "admin@example.test"]);

        Assert.Equal("admin", parsed.Username);
        Assert.Equal("admin@example.test", parsed.Email);
        Assert.Equal("Empresa principal", parsed.CompanyName);
        Assert.Equal("Sucursal principal", parsed.BranchName);
    }

    [Fact]
    public void ParseRejectsPasswordArgumentsWithoutEchoingTheirValue()
    {
        const string secret = "NeverEchoThisPassword!42";

        var error = Assert.Throws<ArgumentException>(() => BootstrapArgumentParser.Parse(
            ["create-admin", "--username", "admin", "--email", "admin@example.test", "--password", secret]));

        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("12345678901")]
    public void PasswordPolicyRejectsShortPasswords(string password)
    {
        Assert.False(BootstrapPasswordPolicy.IsValid(password));
    }

    [Fact]
    public void PasswordPolicyAcceptsTwelveCharactersWithinInputByteLimit()
    {
        Assert.True(BootstrapPasswordPolicy.IsValid("correct horse"));
        Assert.False(BootstrapPasswordPolicy.IsValid(new string('a', 1025)));
    }
}
