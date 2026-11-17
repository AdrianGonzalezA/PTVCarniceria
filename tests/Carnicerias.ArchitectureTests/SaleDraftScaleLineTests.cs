using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class SaleDraftScaleLineTests
{
    [Fact]
    public void KeepsIndividualCutsWithTheSameProductAndDifferentScaleReferences()
    {
        var company = Guid.NewGuid();
        var product = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = new SaleDraftLine(company, product, "1002", "Asado", "kg",
            ProductSaleMode.Weight, 0.750m, 4900m, scaleLabelReference: "EAN:2000123007502");
        var second = new SaleDraftLine(company, product, "1002", "Asado", "kg",
            ProductSaleMode.Weight, 0.500m, 4900m, scaleLabelReference: "EAN:2000123005003");
        var draft = new SaleDraft(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

        draft.ReplaceLines([first, second], now);
        Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(1.250m, draft.Lines.Sum(line => line.Quantity));

        draft.ReplaceLines([
            new SaleDraftLine(company, product, "1002", "Asado", "kg", ProductSaleMode.Weight,
                0.750m, 5500m, scaleLabelReference: first.ScaleLabelReference),
            new SaleDraftLine(company, product, "1002", "Asado", "kg", ProductSaleMode.Weight,
                0.500m, 5500m, scaleLabelReference: second.ScaleLabelReference),
        ], now.AddMinutes(1));
        Assert.Equal([first.Id, second.Id], draft.Lines.Select(line => line.Id));
        Assert.All(draft.Lines, line => Assert.Equal(4900m, line.UnitPrice));
    }
}
