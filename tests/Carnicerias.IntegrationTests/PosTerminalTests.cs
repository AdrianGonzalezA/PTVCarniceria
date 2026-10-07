using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class PosTerminalTests
{
    [Fact]
    public void TerminalBelongsToOneBranchAndHasStableIdentity()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var first = new PosTerminal(companyId, branchId, " Caja 1 ");
        var second = new PosTerminal(companyId, branchId, "Caja 2");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(companyId, first.CompanyId);
        Assert.Equal(branchId, first.BranchId);
        Assert.Equal("Caja 1", first.Name);
        Assert.True(first.IsActive);
        Assert.False(first.IsHistorical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new PosTerminal(Guid.NewGuid(), Guid.NewGuid(), name));
    }

    [Fact]
    public void DatabaseModelConstrainsTerminalToItsCompanyAndBranch()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        var terminal = db.Model.FindEntityType(typeof(PosTerminal));

        Assert.NotNull(terminal);
        Assert.Equal("pos_terminals", terminal.GetTableName());
        Assert.Contains(terminal.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(Branch) &&
            key.Properties.Select(property => property.Name).SequenceEqual([
                nameof(PosTerminal.CompanyId), nameof(PosTerminal.BranchId)]));
        Assert.Contains(terminal.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(PosTerminal.CompanyId), nameof(PosTerminal.BranchId), nameof(PosTerminal.Name)]));
    }
}
