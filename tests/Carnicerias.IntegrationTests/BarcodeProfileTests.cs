using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class BarcodeProfileTests
{
    [Fact]
    public void ProfilePreservesAnImmutableFormulaAndWeightScale()
    {
        var companyId = Guid.NewGuid();
        var profile = new BarcodeProfile(companyId, "Ciclo 2", 1,
            "pro_numero(5) pro_item(3) peso(4)", "peso", 2, DateTime.UtcNow);

        Assert.Equal(companyId, profile.CompanyId);
        Assert.Equal("Ciclo 2", profile.Name);
        Assert.Equal(1, profile.Revision);
        Assert.Equal(2, profile.WeightDecimals);
        Assert.Equal("peso", profile.WeightField);
    }

    [Theory]
    [InlineData("pro_numero(5) peso(4)", "ausente", 2)]
    [InlineData("pro_numero(5) peso(4)", "peso", 5)]
    [InlineData("pro_numero(5) peso(4)", "peso", -1)]
    public void ProfileRejectsAnUnusableWeightDefinition(string formula, string field, int decimals)
    {
        Assert.Throws<ArgumentException>(() => new BarcodeProfile(Guid.NewGuid(), "Origen", 1,
            formula, field, decimals, DateTime.UtcNow));
    }

    [Fact]
    public void ProfileRevisionsAreUniqueWithinACompanyAndName()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres").Options;
        using var db = new PlatformAccessDbContext(options);
        var entity = db.Model.FindEntityType(typeof(BarcodeProfile));

        Assert.NotNull(entity);
        Assert.Equal("inventory", entity.GetSchema());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(BarcodeProfile.CompanyId), nameof(BarcodeProfile.NormalizedName),
                nameof(BarcodeProfile.Revision)]));
    }
}
