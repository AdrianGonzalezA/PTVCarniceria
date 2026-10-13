using Carnicerias.Bootstrap;

namespace Carnicerias.ArchitectureTests;

public sealed class CompanyAdminBootstrapArgumentTests
{
    [Fact]
    public void ParsesRequiredCompanyAndIdentity()
    {
        var parsed = CompanyAdminBootstrapArguments.Parse([
            "create-company-admin", "--company-id", "102ee262-53e1-48ed-9756-7c2c84bccb44",
            "--username", "rustica-admin", "--email", "rustica-admin@local.test"]);

        Assert.Equal(Guid.Parse("102ee262-53e1-48ed-9756-7c2c84bccb44"), parsed.CompanyId);
        Assert.Equal("rustica-admin", parsed.Username);
        Assert.Equal("rustica-admin@local.test", parsed.Email);
    }

    [Theory]
    [InlineData("--company-id", "not-a-guid")]
    [InlineData("--username", "cashier@example.test")]
    [InlineData("--email", "not-an-email")]
    public void RejectsInvalidValues(string option, string invalidValue)
    {
        var args = new[] { "create-company-admin", "--company-id", Guid.NewGuid().ToString(),
            "--username", "rustica-admin", "--email", "rustica-admin@local.test" };
        args[Array.IndexOf(args, option) + 1] = invalidValue;

        Assert.Throws<ArgumentException>(() => CompanyAdminBootstrapArguments.Parse(args));
    }

    [Fact]
    public void RejectsDuplicateOptions()
    {
        Assert.Throws<ArgumentException>(() => CompanyAdminBootstrapArguments.Parse([
            "create-company-admin", "--company-id", Guid.NewGuid().ToString(),
            "--username", "one", "--username", "two", "--email", "one@local.test"]));
    }
}
