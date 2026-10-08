namespace Carnicerias.Domain.Sales;

public sealed record OutstandingAccountSale(Guid SaleId, DateTimeOffset SoldAtUtc, decimal OutstandingAmount);
public sealed record AccountAllocationChoice(Guid SaleId, decimal Amount);
public sealed record AccountCollectionAllocation(Guid SaleId, decimal Amount);
public sealed record AccountCollectionPlanResult(
    IReadOnlyList<AccountCollectionAllocation> Allocations, decimal CreditAmount);

public sealed class AccountAllocationException : InvalidOperationException
{
    public AccountAllocationException() : base("The collection cannot be allocated to these sales.") { }
}

public static class CustomerCollectionPlan
{
    private const decimal MaximumAmount = 9_999_999_999.99m;

    public static AccountCollectionPlanResult Suggest(
        decimal collectionAmount, IReadOnlyList<OutstandingAccountSale> sales)
    {
        ValidateInput(collectionAmount, sales);
        var remaining = collectionAmount;
        var allocations = new List<AccountCollectionAllocation>();
        foreach (var sale in sales.OrderBy(item => item.SoldAtUtc).ThenBy(item => item.SaleId))
        {
            if (remaining == 0) break;
            var applied = Math.Min(remaining, sale.OutstandingAmount);
            if (applied == 0) continue;
            allocations.Add(new AccountCollectionAllocation(sale.SaleId, applied));
            remaining -= applied;
        }
        return new AccountCollectionPlanResult(allocations, remaining);
    }

    public static AccountCollectionPlanResult Choose(
        decimal collectionAmount, IReadOnlyList<OutstandingAccountSale> sales,
        IReadOnlyList<AccountAllocationChoice> choices)
    {
        ValidateInput(collectionAmount, sales);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Select(item => item.SaleId).Distinct().Count() != choices.Count)
            throw new AccountAllocationException();

        var outstanding = sales.ToDictionary(item => item.SaleId, item => item.OutstandingAmount);
        var allocations = new List<AccountCollectionAllocation>(choices.Count);
        decimal allocated = 0;
        foreach (var choice in choices)
        {
            if (!outstanding.TryGetValue(choice.SaleId, out var due) || !IsValidAmount(choice.Amount) ||
                choice.Amount == 0 || choice.Amount > due)
                throw new AccountAllocationException();
            allocated += choice.Amount;
            if (allocated > collectionAmount) throw new AccountAllocationException();
            allocations.Add(new AccountCollectionAllocation(choice.SaleId, choice.Amount));
        }
        return new AccountCollectionPlanResult(allocations, collectionAmount - allocated);
    }

    private static void ValidateInput(decimal amount, IReadOnlyList<OutstandingAccountSale> sales)
    {
        ArgumentNullException.ThrowIfNull(sales);
        if (!IsValidAmount(amount) || amount == 0 ||
            sales.Any(item => item.SaleId == Guid.Empty || !IsValidAmount(item.OutstandingAmount)) ||
            sales.Select(item => item.SaleId).Distinct().Count() != sales.Count)
            throw new AccountAllocationException();
    }

    private static bool IsValidAmount(decimal amount) =>
        amount >= 0 && amount <= MaximumAmount && decimal.Round(amount, 2) == amount;
}
