using Carnicerias.Domain.Sales;
using Carnicerias.Infrastructure;

namespace Carnicerias.IntegrationTests;

public sealed class CustomerCollectionCorrectionTests
{
    [Fact]
    public void ReallocationKeepsOriginalAndReplacementLinkedWithoutChangingTheirAmounts()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var hash = new string('A', 64);
        var original = new CustomerCollectionReceipt(companyId, branchId, customerId,
            cashierId, shiftId, terminalId, Guid.NewGuid(), hash, PaymentMethod.Cash,
            500m, 100m, now);
        var replacement = new CustomerCollectionReceipt(companyId, branchId, customerId,
            cashierId, shiftId, terminalId, Guid.NewGuid(), hash, PaymentMethod.Cash,
            500m, 0m, now, CollectionReceiptOrigin.Reallocation, original.Id);
        var correction = new CustomerCollectionCorrection(companyId, branchId, original.Id,
            replacement.Id, cashierId, shiftId, terminalId, Guid.NewGuid(), hash,
            CustomerCollectionCorrectionKind.Reallocate, "  Cuenta equivocada  ", 500m, now);

        original.Void(now);

        Assert.True(original.IsVoided);
        Assert.Equal(now, original.VoidedAtUtc);
        Assert.Equal(500m, original.Amount);
        Assert.Equal(original.Id, replacement.ReplacesReceiptId);
        Assert.Equal(CollectionReceiptOrigin.Reallocation, replacement.Origin);
        Assert.Equal(original.Id, correction.OriginalReceiptId);
        Assert.Equal(replacement.Id, correction.ReplacementReceiptId);
        Assert.Equal("Cuenta equivocada", correction.Reason);
        Assert.Throws<InvalidOperationException>(() => original.Void(now));
    }

    [Fact]
    public void RefundHasNoReplacementAndRejectsInvalidKindReferenceCombinations()
    {
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var now = DateTimeOffset.UtcNow;
        var hash = new string('B', 64);

        var refund = new CustomerCollectionCorrection(ids[0], ids[1], ids[2], null,
            ids[3], ids[4], ids[5], ids[6], hash,
            CustomerCollectionCorrectionKind.Refund, "Dinero devuelto", 125m, now);

        Assert.Null(refund.ReplacementReceiptId);
        Assert.Equal(125m, refund.Amount);
        Assert.Throws<ArgumentException>(() => new CustomerCollectionCorrection(ids[0], ids[1], ids[2],
            null, ids[3], ids[4], ids[5], ids[6], hash,
            CustomerCollectionCorrectionKind.Reallocate, "Cuenta equivocada", 125m, now));
        Assert.Throws<ArgumentException>(() => new CustomerCollectionCorrection(ids[0], ids[1], ids[2],
            Guid.NewGuid(), ids[3], ids[4], ids[5], ids[6], hash,
            CustomerCollectionCorrectionKind.Refund, "Dinero devuelto", 125m, now));
    }
}
