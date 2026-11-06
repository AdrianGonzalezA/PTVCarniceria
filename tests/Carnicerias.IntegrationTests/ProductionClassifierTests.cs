using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class ProductionClassifierTests
{
    [Fact]
    public void ClassifierCanBeRenamedAndDisabledWithoutLosingIdentity()
    {
        var classifier = new ProductionClassifier(Guid.NewGuid(), "Milanesas");
        var id = classifier.Id;
        classifier.Rename(" Hamburguesas ");
        classifier.Deactivate();

        Assert.Equal(id, classifier.Id);
        Assert.Equal("Hamburguesas", classifier.Name);
        Assert.Equal("HAMBURGUESAS", classifier.NormalizedName);
        Assert.False(classifier.IsActive);
        Assert.Throws<ArgumentException>(() => classifier.Rename(" "));
    }
}
