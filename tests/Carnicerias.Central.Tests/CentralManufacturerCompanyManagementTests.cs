using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralManufacturerCompanyManagementTests
{
    [Fact]
    public void CompanyCreationPreparesIndependentTenantAndAuditActor()
    {
        var hasher = new CentralPasswordHasher();
        var manufacturerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        var plan = CentralCompanyManagementService.PrepareCompany(
            "rustica-central", "LA Rústica", "admin-rustica",
            "a distinct tenant password", hasher, manufacturerId, now);

        Assert.Equal("RUSTICA-CENTRAL", plan.Company.Code);
        Assert.Equal(plan.Company.Id, plan.Administrator.CentralCompanyId);
        Assert.True(hasher.Verify("a distinct tenant password", plan.Administrator.PasswordHash));
        Assert.All(plan.AuditEvents, audit => Assert.Equal(manufacturerId, audit.ActorId));
        Assert.All(plan.AuditEvents, audit => Assert.Equal(plan.Company.Id, audit.CentralCompanyId));
    }

    [Fact]
    public void SixCharacterPasswordIsAllowedOnlyForTheExistingVisualDatabase()
    {
        Assert.Equal(6, CentralPasswordPolicy.MinimumLength(
            "Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres"));
        Assert.Equal(12, CentralPasswordPolicy.MinimumLength(
            "Host=127.0.0.1;Port=5432;Database=some_other_database;Username=postgres"));
    }
}
