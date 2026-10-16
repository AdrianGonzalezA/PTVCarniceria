using Carnicerias.Central.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Central.Tests;

public sealed class CentralIdentityModelTests
{
    [Fact]
    public void CompanyCodeIsNormalizedAndCannotBeChanged()
    {
        var company = CentralCompany.Create("  rustica  ", "La Rústica");

        Assert.Equal("RUSTICA", company.Code);
        Assert.True(company.IsActive);
        Assert.Throws<ArgumentException>(() => CentralCompany.Create("bad code", "Empresa"));
    }

    [Fact]
    public void AdministratorAndSessionRemainBoundToOneCentralCompany()
    {
        var company = CentralCompany.Create("RUSTICA", "La Rústica");
        var administrator = CentralAdministrator.Create(company.Id, "admin", "$argon2id$example");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var session = CentralSession.Create(administrator.Id, company.Id, new string('A', 64), now);

        Assert.Equal(company.Id, administrator.CentralCompanyId);
        Assert.Equal(company.Id, session.CentralCompanyId);
        Assert.True(session.IsActiveAt(now.AddMinutes(1)));
        Assert.False(session.IsActiveAt(now.AddHours(8)));
        session.Revoke(now.AddMinutes(2));
        Assert.False(session.IsActiveAt(now.AddMinutes(3)));
    }

    [Fact]
    public void EfModelMapsOnlyCentralSchemaAndScopedUniqueKeys()
    {
        var options = new DbContextOptionsBuilder<CentralAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")
            .Options;
        using var db = new CentralAccessDbContext(options);

        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Equal("central_access", entity.GetSchema()));
        var companies = db.Model.FindEntityType(typeof(CentralCompany))!;
        Assert.Contains(companies.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["Code"]));
        var administrators = db.Model.FindEntityType(typeof(CentralAdministrator))!;
        Assert.Contains(administrators.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CentralCompanyId", "UsernameNormalized"]));
    }
}
