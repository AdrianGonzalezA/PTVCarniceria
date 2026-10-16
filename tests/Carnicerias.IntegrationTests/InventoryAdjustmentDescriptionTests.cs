using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class InventoryAdjustmentDescriptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDescriptionKeepsAnAuditableDefault(string? description)
    {
        Assert.Equal("Ajuste manual", InventoryMovement.NormalizeAdjustmentDescription(description));
    }

    [Fact]
    public void DescriptionIsTrimmed()
    {
        Assert.Equal("Conteo de mostrador", InventoryMovement.NormalizeAdjustmentDescription("  Conteo de mostrador  "));
    }
}
