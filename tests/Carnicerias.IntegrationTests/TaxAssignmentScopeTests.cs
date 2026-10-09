using Carnicerias.Api.Catalog;

namespace Carnicerias.IntegrationTests;

public sealed class TaxAssignmentScopeTests
{
    [Fact]
    public void AllIncludesEveryExistingProductAndRequiresMatchingPreviewCount()
    {
        var active = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        var result = TaxAssignmentScope.Resolve("all", null, 2, [active, inactive]);

        Assert.Equal(2, result.Length);
        Assert.Contains(active, result);
        Assert.Contains(inactive, result);
        Assert.Throws<InvalidOperationException>(() =>
            TaxAssignmentScope.Resolve("all", null, 1, [active, inactive]));
    }

    [Fact]
    public void SelectedRejectsForeignOrDuplicateProductIds()
    {
        var own = Guid.NewGuid();
        var foreign = Guid.NewGuid();

        Assert.Throws<KeyNotFoundException>(() =>
            TaxAssignmentScope.Resolve("selected", [own, foreign], null, [own]));
        Assert.Throws<ArgumentException>(() =>
            TaxAssignmentScope.Resolve("selected", [own, own], null, [own]));
    }
}
