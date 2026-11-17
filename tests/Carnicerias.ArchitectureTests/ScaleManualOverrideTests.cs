using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class ScaleManualOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AppliesOnceToTheAuthorizedProductAndWeight()
    {
        var productId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var grant = Create(productId);
        Assert.False(grant.CanApply(draftId, Guid.NewGuid(), 0.750m, Now));
        Assert.True(grant.CanApply(draftId, productId, 0.750m, Now.AddMinutes(1)));
        grant.Apply(draftId, productId, 0.750m, Now.AddMinutes(1));
        Assert.Equal(draftId, grant.AppliedSaleDraftId);
        Assert.Equal(0.750m, grant.AppliedWeightKg);
        Assert.False(grant.CanApply(Guid.NewGuid(), productId, 0.750m, Now.AddMinutes(2)));
    }

    [Fact]
    public void ExpiresAndCannotApproveTheCashier()
    {
        var productId = Guid.NewGuid();
        var grant = Create(productId);
        Assert.False(grant.CanApply(Guid.NewGuid(), productId, 0.750m, Now.AddMinutes(16)));
        var cashierId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new ScaleManualOverride(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), cashierId, productId,
            cashierId, "Balanza desconectada", Now));
    }

    private static ScaleManualOverride Create(Guid productId) => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), productId,
        Guid.NewGuid(), "Balanza desconectada", Now);
}
