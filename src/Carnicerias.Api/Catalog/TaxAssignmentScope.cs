namespace Carnicerias.Api.Catalog;

public static class TaxAssignmentScope
{
    public static Guid[] Resolve(string? scope, Guid[]? selectedIds,
        int? expectedProductCount, IReadOnlyList<Guid> companyProductIds)
    {
        if (scope == "all")
        {
            if (selectedIds is { Length: > 0 } || expectedProductCount is null or < 0)
                throw new ArgumentException("All scope requires a preview count, not selected IDs.");
            if (expectedProductCount != companyProductIds.Count)
                throw new InvalidOperationException("Product count changed after the preview.");
            return companyProductIds.Order().ToArray();
        }
        if (scope != "selected" || selectedIds is not { Length: > 0 and <= 1000 } ||
            expectedProductCount is not null || selectedIds.Contains(Guid.Empty) ||
            selectedIds.Distinct().Count() != selectedIds.Length)
            throw new ArgumentException("Selected scope requires distinct product IDs.");

        var ownIds = companyProductIds.ToHashSet();
        if (selectedIds.Any(id => !ownIds.Contains(id)))
            throw new KeyNotFoundException("One or more products are outside the company.");
        return selectedIds.Order().ToArray();
    }
}
