using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class OtherTaxAssignmentTests
{
    [Fact]
    public void ClosingAnAssignmentKeepsItsOriginalActorAndVigency()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var end = DateTimeOffset.UtcNow;
        var actorId = Guid.NewGuid();
        var assignment = new OtherTaxAssignment(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), actorId, start);

        assignment.Close(end, Guid.NewGuid());

        Assert.Equal(start, assignment.EffectiveFromUtc);
        Assert.Equal(end, assignment.EffectiveToUtc);
        Assert.Equal(actorId, assignment.AssignedByUserId);
        Assert.NotNull(assignment.RemovedByUserId);
        Assert.Throws<InvalidOperationException>(() => assignment.Close(end.AddMinutes(1), Guid.NewGuid()));
    }
}
