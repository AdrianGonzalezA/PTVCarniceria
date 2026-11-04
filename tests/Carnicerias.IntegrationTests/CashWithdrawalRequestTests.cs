using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CashWithdrawalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestDoesNotDebitCashUntilAnotherAdministratorApproves()
    {
        var cashierId = Guid.NewGuid();
        var request = Create(cashierId);

        Assert.Equal(CashWithdrawalStatus.Pending, request.Status);
        Assert.Equal(PaymentMethod.Cash, request.Method);
        Assert.Null(request.LedgerMovementId);
        Assert.Throws<InvalidOperationException>(() =>
            request.Approve(cashierId, Guid.NewGuid(), 100m, Now.AddMinutes(1)));

        var ledgerId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() =>
            request.Approve(approverId, ledgerId, 99m, Now.AddMinutes(2)));
        Assert.Equal(CashWithdrawalStatus.Pending, request.Status);
        request.Approve(approverId, ledgerId, 100m, Now.AddMinutes(2));

        Assert.Equal(CashWithdrawalStatus.Approved, request.Status);
        Assert.Equal(ledgerId, request.LedgerMovementId);
        Assert.Equal(approverId, request.DecidedByUserId);
        Assert.Throws<InvalidOperationException>(() =>
            request.Approve(approverId, Guid.NewGuid(), 100m, Now.AddMinutes(3)));
    }

    [Fact]
    public void RejectedRequestHasNoCashMovementAndCannotLaterBeApproved()
    {
        var request = Create(Guid.NewGuid());
        request.Reject(Guid.NewGuid(), "No hay efectivo suficiente", Now.AddMinutes(1));

        Assert.Equal(CashWithdrawalStatus.Rejected, request.Status);
        Assert.Null(request.LedgerMovementId);
        Assert.Equal("No hay efectivo suficiente", request.DecisionReason);
        Assert.Throws<InvalidOperationException>(() =>
            request.Approve(Guid.NewGuid(), Guid.NewGuid(), 100m, Now.AddMinutes(2)));
    }

    [Fact]
    public void LedgerWithdrawalMustBeCashNegativeAndUnrelatedToSale()
    {
        var request = Create(Guid.NewGuid());
        var movement = new CashLedgerMovement(request.CompanyId, request.BranchId,
            request.CashierShiftId, request.RequestedByUserId, request.OperationId,
            PaymentMethod.Cash, CashLedgerMovementKind.ExtraordinaryWithdrawal,
            -request.Amount, Now, posTerminalId: request.PosTerminalId);

        Assert.Equal(-request.Amount, movement.AmountDelta);
        Assert.Throws<ArgumentException>(() => new CashLedgerMovement(request.CompanyId,
            request.BranchId, request.CashierShiftId, request.RequestedByUserId,
            Guid.NewGuid(), PaymentMethod.Debit, CashLedgerMovementKind.ExtraordinaryWithdrawal,
            -request.Amount, Now, posTerminalId: request.PosTerminalId));
        Assert.Throws<ArgumentException>(() => new CashLedgerMovement(request.CompanyId,
            request.BranchId, request.CashierShiftId, request.RequestedByUserId,
            Guid.NewGuid(), PaymentMethod.Cash, CashLedgerMovementKind.ExtraordinaryWithdrawal,
            request.Amount, Now, posTerminalId: request.PosTerminalId));
    }

    private static CashWithdrawalRequest Create(Guid cashierId) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), cashierId,
        Guid.NewGuid(), 100m, "Retiro autorizado", Now);
}
