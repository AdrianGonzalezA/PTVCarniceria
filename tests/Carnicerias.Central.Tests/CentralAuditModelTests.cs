using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Tests;

public sealed class CentralAuditModelTests
{
    [Fact]
    public void RecordsOnlyTypedAuditFactsWithoutAFreeTextPayload()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        var audit = CentralAuditEvent.Record(CentralAuditAction.AdministratorCreated,
            CentralAuditResult.Succeeded, companyId, actorId, now);

        Assert.Equal(companyId, audit.CentralCompanyId);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal(CentralAuditAction.AdministratorCreated, audit.Action);
        Assert.Equal(now, audit.OccurredAtUtc);
        Assert.DoesNotContain(typeof(CentralAuditEvent).GetProperties(), property =>
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Detail", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditTableIsConfinedToTheCentralSchema()
    {
        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")
            .Options;
        using var db = new CentralAccessDbContext(options);

        var audit = db.Model.FindEntityType(typeof(CentralAuditEvent))!;
        Assert.Equal("central_access", audit.GetSchema());
        Assert.Equal("audit_events", audit.GetTableName());
    }
}
