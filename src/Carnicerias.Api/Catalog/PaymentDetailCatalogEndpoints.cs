using Carnicerias.Api.Security;
using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.Catalog;

public static class PaymentDetailCatalogEndpoints
{
    public static IEndpointRouteBuilder MapPaymentDetailCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalog/payment-details", GetAsync)
            .RequireOperationalContext();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(PlatformAccessDbContext db,
        CancellationToken cancellationToken)
    {
        var brands = await db.PaymentCardBrands.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new CardBrandOption(item.Code, item.Name,
                item.SupportsDebit, item.SupportsCredit))
            .ToArrayAsync(cancellationToken);
        var processors = await db.PaymentProcessors.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new PaymentDetailOption(item.Code, item.Name))
            .ToArrayAsync(cancellationToken);
        var banks = await db.FinancialInstitutions.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new PaymentDetailOption(item.Code, item.Name))
            .ToArrayAsync(cancellationToken);
        var wallets = await db.PaymentWallets.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new PaymentDetailOption(item.Code, item.Name))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new PaymentDetailCatalogResponse(brands, processors, banks, wallets));
    }

    public sealed record PaymentDetailOption(string Id, string Name);
    public sealed record CardBrandOption(string Id, string Name, bool SupportsDebit, bool SupportsCredit);
    public sealed record PaymentDetailCatalogResponse(
        IReadOnlyList<CardBrandOption> CardBrands,
        IReadOnlyList<PaymentDetailOption> Processors,
        IReadOnlyList<PaymentDetailOption> Banks,
        IReadOnlyList<PaymentDetailOption> Wallets);
}
