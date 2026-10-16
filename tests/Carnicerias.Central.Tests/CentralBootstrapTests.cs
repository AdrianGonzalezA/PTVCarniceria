using Carnicerias.Central.Bootstrap;
using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Tests;

public sealed class CentralBootstrapTests
{
    [Fact]
    public void PreparesIndependentCompanyAndFirstAdministratorWithoutCleartext()
    {
        var hasher = new CentralPasswordHasher();
        var now = DateTimeOffset.UtcNow;
        var plan = CentralBootstrapService.PrepareCompany(
            " visual-central ", "Visual Central", "admin-central",
            "a unique laboratory password", hasher, now);

        Assert.Equal("VISUAL-CENTRAL", plan.Company.Code);
        Assert.Equal(plan.Company.Id, plan.Administrator.CentralCompanyId);
        Assert.True(hasher.Verify("a unique laboratory password", plan.Administrator.PasswordHash));
        Assert.NotEqual("a unique laboratory password", plan.Administrator.PasswordHash);
        Assert.Equal(2, plan.AuditEvents.Count);
        Assert.All(plan.AuditEvents, audit => Assert.Equal(plan.Company.Id, audit.CentralCompanyId));
    }

    [Fact]
    public void ResetRevokesSessionsAndRecordsAudit()
    {
        var hasher = new CentralPasswordHasher();
        var company = CentralCompany.Create("VISUAL", "Visual");
        var administrator = CentralAdministrator.Create(
            company.Id, "central-admin", hasher.Hash("the previous password"));
        var now = DateTimeOffset.UtcNow;
        var session = CentralSession.Create(
            administrator.Id, company.Id, new string('A', 64), now.AddMinutes(-5));

        var audit = CentralBootstrapService.ApplyPasswordReset(
            administrator, [session], "a new laboratory password", hasher, now);

        Assert.True(hasher.Verify("a new laboratory password", administrator.PasswordHash));
        Assert.False(session.IsActiveAt(now));
        Assert.Equal(CentralAuditAction.PasswordReset, audit.Action);
        Assert.Equal(company.Id, audit.CentralCompanyId);
    }
}
