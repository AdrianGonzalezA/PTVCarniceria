using System.Security.Cryptography;
using Carnicerias.Api.Federation;
using Microsoft.Extensions.Configuration;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchServiceSettingsTests
{
    private const string CurrentToken = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string NextToken = "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f";

    [Fact]
    public void IsDisabledWhenNotConfiguredOrIncomplete()
    {
        Assert.False(BranchServiceSettings.FromConfiguration(Configuration()).IsEnabled);

        var incomplete = Configuration(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "true",
            ["Federation:InstallationId"] = Guid.NewGuid().ToString(),
            ["Federation:CompanyId"] = Guid.NewGuid().ToString(),
            ["Federation:CredentialSha256"] = Hash(CurrentToken)
        });

        Assert.False(BranchServiceSettings.FromConfiguration(incomplete).IsEnabled);
    }

    [Fact]
    public void AcceptsCurrentAndNextCredentialButRejectsOtherTokens()
    {
        var installationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var settings = BranchServiceSettings.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "true",
            ["Federation:InstallationId"] = installationId.ToString(),
            ["Federation:CompanyId"] = companyId.ToString(),
            ["Federation:BranchId"] = branchId.ToString(),
            ["Federation:CredentialSha256"] = Hash(CurrentToken),
            ["Federation:NextCredentialSha256"] = Hash(NextToken)
        }));

        Assert.True(settings.IsEnabled);
        Assert.Equal(installationId, settings.InstallationId);
        Assert.Equal(companyId, settings.CompanyId);
        Assert.Equal(branchId, settings.BranchId);
        Assert.True(settings.VerifyCredential(CurrentToken));
        Assert.True(settings.VerifyCredential(NextToken));
        Assert.False(settings.VerifyCredential(new string('f', 64)));
        Assert.False(settings.VerifyCredential("short"));
    }

    [Fact]
    public void RejectsMalformedVerifier()
    {
        var settings = BranchServiceSettings.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "true",
            ["Federation:InstallationId"] = Guid.NewGuid().ToString(),
            ["Federation:CompanyId"] = Guid.NewGuid().ToString(),
            ["Federation:BranchId"] = Guid.NewGuid().ToString(),
            ["Federation:CredentialSha256"] = "not-a-hash"
        }));

        Assert.False(settings.IsEnabled);
        Assert.False(settings.VerifyCredential(CurrentToken));
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Convert.FromHexString(token)));
}
