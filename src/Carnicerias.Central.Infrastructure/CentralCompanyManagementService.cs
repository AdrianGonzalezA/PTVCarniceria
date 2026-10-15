using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.Central.Infrastructure;

public sealed record CentralCompanyCreationPlan(CentralCompany Company,
    CentralAdministrator Administrator, IReadOnlyList<CentralAuditEvent> AuditEvents);
public sealed record CentralManagedCompany(Guid Id, string Code, string Name,
    bool IsActive, int AdministratorCount, int InstallationCount);
public sealed record CentralManagedAdministrator(Guid Id, string Username, bool IsActive);
public sealed record CentralCreatedCompany(Guid Id, string Code, string Name,
    Guid InitialAdministratorId);
public sealed record CentralManagementPage<T>(IReadOnlyList<T> Data, int Page,
    int PageSize, int TotalCount);

public sealed class CentralManagementConflictException : Exception;

public interface ICentralCompanyManagementService
{
    Task<CentralManagementPage<CentralManagedCompany>> ListCompaniesAsync(
        int page, int pageSize, CancellationToken cancellationToken);
    Task<CentralManagementPage<CentralManagedAdministrator>?> ListAdministratorsAsync(
        Guid companyId, int page, int pageSize, CancellationToken cancellationToken);
    Task<CentralCreatedCompany> CreateCompanyAsync(string code, string name,
        string username, string password, Guid actorId, CancellationToken cancellationToken);
    Task<CentralManagedAdministrator?> AddAdministratorAsync(Guid companyId,
        string username, string password, Guid actorId, CancellationToken cancellationToken);
}

public sealed class CentralCompanyManagementService(CentralAccessDbContext database,
    ICentralPasswordHasher hasher, TimeProvider clock) : ICentralCompanyManagementService
{
    public static CentralCompanyCreationPlan PrepareCompany(string code, string name,
        string username, string password, ICentralPasswordHasher hasher,
        Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        var company = CentralCompany.Create(code, name);
        var administrator = CentralAdministrator.Create(company.Id, username,
            hasher.Hash(password));
        var audit = new[]
        {
            CentralAuditEvent.Record(CentralAuditAction.CompanyCreated,
                CentralAuditResult.Succeeded, company.Id, actorId, now),
            CentralAuditEvent.Record(CentralAuditAction.AdministratorCreated,
                CentralAuditResult.Succeeded, company.Id, actorId, now)
        };
        return new CentralCompanyCreationPlan(company, administrator, audit);
    }

    public async Task<CentralManagementPage<CentralManagedCompany>> ListCompaniesAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var companies = database.Companies.AsNoTracking();
        var count = await companies.CountAsync(cancellationToken);
        var data = await companies.OrderBy(company => company.Name).ThenBy(company => company.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(company => new CentralManagedCompany(company.Id, company.Code, company.Name,
                company.IsActive,
                database.Administrators.Count(admin => admin.CentralCompanyId == company.Id),
                database.Installations.Count(installation =>
                    installation.CentralCompanyId == company.Id && installation.IsActive)))
            .ToArrayAsync(cancellationToken);
        return new CentralManagementPage<CentralManagedCompany>(data, page, pageSize, count);
    }

    public async Task<CentralManagementPage<CentralManagedAdministrator>?> ListAdministratorsAsync(
        Guid companyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await database.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId,
                cancellationToken)) return null;
        var admins = database.Administrators.AsNoTracking()
            .Where(admin => admin.CentralCompanyId == companyId);
        var count = await admins.CountAsync(cancellationToken);
        var data = await admins.OrderBy(admin => admin.UsernameNormalized).ThenBy(admin => admin.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(admin => new CentralManagedAdministrator(admin.Id, admin.Username,
                admin.IsActive)).ToArrayAsync(cancellationToken);
        return new CentralManagementPage<CentralManagedAdministrator>(data, page, pageSize, count);
    }

    public async Task<CentralCreatedCompany> CreateCompanyAsync(string code, string name,
        string username, string password, Guid actorId, CancellationToken cancellationToken)
    {
        var plan = PrepareCompany(code, name, username, password, hasher,
            actorId, clock.GetUtcNow());
        database.Companies.Add(plan.Company);
        database.Administrators.Add(plan.Administrator);
        database.AuditEvents.AddRange(plan.AuditEvents);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        { throw new CentralManagementConflictException(); }
        return new CentralCreatedCompany(plan.Company.Id, plan.Company.Code,
            plan.Company.Name, plan.Administrator.Id);
    }

    public async Task<CentralManagedAdministrator?> AddAdministratorAsync(Guid companyId,
        string username, string password, Guid actorId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Company and actor are required.");
        var company = await database.Companies.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == companyId, cancellationToken);
        if (company is null) return null;
        if (!company.IsActive) throw new CentralManagementConflictException();

        var admin = CentralAdministrator.Create(companyId, username, hasher.Hash(password));
        database.Administrators.Add(admin);
        database.AuditEvents.Add(CentralAuditEvent.Record(
            CentralAuditAction.AdministratorCreated, CentralAuditResult.Succeeded,
            companyId, actorId, clock.GetUtcNow()));
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        { throw new CentralManagementConflictException(); }
        return new CentralManagedAdministrator(admin.Id, admin.Username, admin.IsActive);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation };
}
