using Carnicerias.Infrastructure;

namespace Carnicerias.Api.Federation;

public static class BranchOperationalScope
{
    public static bool AllowsCompany(BranchServiceSettings settings, Guid companyId) =>
        !settings.IsRequested || settings.IsEnabled && settings.CompanyId == companyId;

    public static bool AllowsContext(BranchServiceSettings settings, Guid companyId, Guid branchId) =>
        !settings.IsRequested || settings.IsEnabled &&
        settings.CompanyId == companyId && settings.BranchId == branchId;

    public static IReadOnlyList<OperationalCompanyOption> Restrict(
        IReadOnlyList<OperationalCompanyOption> options, BranchServiceSettings settings)
    {
        if (!settings.IsRequested) return options;
        if (!settings.IsEnabled) return [];

        return options
            .Where(company => company.CompanyId == settings.CompanyId)
            .Select(company => company with
            {
                Branches = company.Branches.Where(branch => branch.BranchId == settings.BranchId).ToArray()
            })
            .Where(company => company.Branches.Count > 0)
            .ToArray();
    }
}
