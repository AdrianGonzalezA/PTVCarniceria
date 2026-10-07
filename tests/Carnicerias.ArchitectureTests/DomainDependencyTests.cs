using Carnicerias.Domain.PlatformAccess;

namespace Carnicerias.ArchitectureTests;

public sealed class DomainDependencyTests
{
    [Fact]
    public void DomainDoesNotReferenceInfrastructure()
    {
        var references = typeof(OperationalContext).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain(
            references,
            name => name is not null && name.Contains("Infrastructure", StringComparison.Ordinal));
    }
}
