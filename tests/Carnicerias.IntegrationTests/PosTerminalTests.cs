using Carnicerias.Infrastructure;
using Carnicerias.Domain.Sales;
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

    [Fact]
    public void OperationalRecordsKeepTheirTerminalAndDraftKeepsItsShift()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var shift = new CashierShift(companyId, branchId, cashierId, 0, now, terminalId);
        var draft = new SaleDraft(companyId, branchId, cashierId, Guid.NewGuid(), now,
            terminalId, shift.Id);
        var sale = new ConfirmedSale(companyId, branchId, cashierId, shift.Id, draft.Id,
            Guid.NewGuid(), 100, new string('a', 64), now, terminalId);
        var movement = new CashLedgerMovement(companyId, branchId, shift.Id, cashierId,
            Guid.NewGuid(), PaymentMethod.Cash, CashLedgerMovementKind.SalePayment,
            100, now, sale.Id, terminalId);

        Assert.Equal(terminalId, shift.PosTerminalId);
        Assert.Equal(terminalId, draft.PosTerminalId);
        Assert.Equal(shift.Id, draft.CashierShiftId);
        Assert.Equal(terminalId, sale.PosTerminalId);
        Assert.Equal(terminalId, movement.PosTerminalId);
    }

    [Fact]
    public void DatabaseModelLinksOperationalRecordsToTerminal()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);

        foreach (var type in new[] { typeof(CashierShift), typeof(SaleDraft),
                     typeof(ConfirmedSale), typeof(CashLedgerMovement) })
        {
            var entity = db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.Contains(entity.GetForeignKeys(), key =>
                key.PrincipalEntityType.ClrType == typeof(PosTerminal) &&
                key.Properties.Select(property => property.Name).SequenceEqual([
                    "CompanyId", "BranchId", "PosTerminalId"]));
        }
    }

    [Fact]
    public void SessionCanBeBoundToOnlyOneTerminal()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(Guid.NewGuid(), new string('a', 64), now, now.AddHours(8));
        var terminalId = Guid.NewGuid();

        session.BindToTerminal(terminalId);

        Assert.Equal(terminalId, session.PosTerminalId);
        Assert.Throws<InvalidOperationException>(() => session.BindToTerminal(Guid.NewGuid()));
    }
}
