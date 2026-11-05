using System.Text.Json;

namespace Carnicerias.Api.Inventory;

public sealed record ShiftClosingPayment(string Method, decimal Amount);

public sealed record ShiftClosingSnapshot(
    int Version,
    Guid ShiftId,
    Guid CompanyId,
    Guid BranchId,
    Guid? TerminalId,
    Guid CashierId,
    string CompanyName,
    string BranchName,
    string? TerminalName,
    string CashierName,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset ClosedAtUtc,
    decimal OpeningCash,
    decimal CashSales,
    decimal NonCashSales,
    decimal AccountSales,
    decimal SalesTotal,
    decimal CashCollections,
    decimal NonCashCollections,
    decimal CreditApplied,
    decimal CashWithdrawals,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? HandedCash,
    decimal? CashDifference,
    IReadOnlyList<ShiftClosingPayment> PaymentBreakdown)
{
    public bool IsComplete => CountedCash.HasValue && HandedCash.HasValue;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
}
