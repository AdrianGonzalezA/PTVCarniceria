using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class TaxCatalogEntryTests
{
    [Fact]
    public void CreatesCompanyScopedEntryWithNormalizedCode()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var entry = new TaxCatalogEntry(companyId, " iva_21_00 ", "IVA 21 %",
            TaxKind.Vat, 21m, actorId, DateTimeOffset.UtcNow);

        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal("IVA_21_00", entry.Code);
        Assert.Equal(TaxKind.Vat, entry.Kind);
        Assert.True(entry.IsActive);
    }

    [Theory]
    [InlineData("", 21)]
    [InlineData("IVA 21", 21)]
    [InlineData("IVA_21", -1)]
    [InlineData("IVA_21", 101)]
    [InlineData("IVA_21", 21.123)]
    public void RejectsInvalidCodeOrRate(string code, decimal rate)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TaxCatalogEntry(Guid.NewGuid(),
            code, "IVA 21 %", TaxKind.Vat, rate, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DeactivationPreservesIdentityAndRecordsActor()
    {
        var actorId = Guid.NewGuid();
        var entry = new TaxCatalogEntry(Guid.NewGuid(), "IIBB_3", "Percepción IIBB",
            TaxKind.Other, 3m, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        var at = DateTimeOffset.UtcNow;

        entry.Deactivate(actorId, at);

        Assert.False(entry.IsActive);
        Assert.Equal(actorId, entry.DeactivatedByUserId);
        Assert.Equal(at, entry.DeactivatedAtUtc);
    }
}
