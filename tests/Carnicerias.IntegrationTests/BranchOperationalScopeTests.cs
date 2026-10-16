using Carnicerias.Api.Federation;
using Carnicerias.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Carnicerias.IntegrationTests;

public sealed class BranchOperationalScopeTests
{
    private static readonly Guid CompanyId = Guid.Parse("102ee262-53e1-48ed-9756-7c2c84bccb44");
    private static readonly Guid BranchId = Guid.Parse("0d5723bb-ac42-4f69-ab3a-4a23591abdf2");
    private static readonly Guid OtherBranchId = Guid.Parse("d7f87213-287a-4b91-b4fc-726d74c717db");

    [Fact]
    public void ManagedInstallationOnlyOffersItsBoundBranch()
    {
        var settings = ManagedSettings();
        var options = new[]
        {
            new OperationalCompanyOption(CompanyId, "La Rústica", [
                new OperationalBranchOption(BranchId, "Alta Córdoba"),
                new OperationalBranchOption(OtherBranchId, "Otra sucursal")]),
            new OperationalCompanyOption(Guid.NewGuid(), "Otra empresa", [
                new OperationalBranchOption(Guid.NewGuid(), "Sucursal ajena")])
        };

        var restricted = BranchOperationalScope.Restrict(options, settings);

        var company = Assert.Single(restricted);
        Assert.Equal(CompanyId, company.CompanyId);
        Assert.Equal(BranchId, Assert.Single(company.Branches).BranchId);
        Assert.True(BranchOperationalScope.AllowsContext(settings, CompanyId, BranchId));
        Assert.False(BranchOperationalScope.AllowsContext(settings, CompanyId, OtherBranchId));
        Assert.False(BranchOperationalScope.AllowsCompany(settings, Guid.NewGuid()));
    }

    [Fact]
    public void IncompleteRequestedBindingFailsClosed()
    {
        var settings = BranchServiceSettings.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Federation:Enabled"] = "true" })
            .Build());
        var options = new[] { new OperationalCompanyOption(CompanyId, "La Rústica", [
            new OperationalBranchOption(BranchId, "Alta Córdoba")]) };

        Assert.Empty(BranchOperationalScope.Restrict(options, settings));
        Assert.False(BranchOperationalScope.AllowsCompany(settings, CompanyId));
        Assert.False(BranchOperationalScope.AllowsContext(settings, CompanyId, BranchId));
    }

    [Fact]
    public void SharedInstallationKeepsEveryAssignedBranchAvailable()
    {
        var settings = BranchServiceSettings.FromConfiguration(new ConfigurationBuilder().Build());
        var options = new[] { new OperationalCompanyOption(CompanyId, "La Rústica", [
            new OperationalBranchOption(BranchId, "Alta Córdoba"),
            new OperationalBranchOption(OtherBranchId, "Otra sucursal")]) };

        Assert.Same(options, BranchOperationalScope.Restrict(options, settings));
        Assert.True(BranchOperationalScope.AllowsCompany(settings, CompanyId));
        Assert.True(BranchOperationalScope.AllowsContext(settings, CompanyId, OtherBranchId));
    }

    private static BranchServiceSettings ManagedSettings() => BranchServiceSettings.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "true",
            ["Federation:Mode"] = "managed",
            ["Federation:CompanyId"] = CompanyId.ToString(),
            ["Federation:BranchId"] = BranchId.ToString()
        }).Build());
}
