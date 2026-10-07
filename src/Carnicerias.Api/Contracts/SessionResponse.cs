namespace Carnicerias.Api.Contracts;

public sealed record SessionResponse(
    Guid UserId,
    string Username,
    DateTimeOffset ExpiresAtUtc,
    OperationalContextResponse? Context);

public sealed record OperationalContextResponse(
    Guid UserId,
    Guid CompanyId,
    string CompanyName,
    Guid BranchId,
    string BranchName,
    IReadOnlyList<string> Permissions,
    Guid SessionId);

public sealed record OperationalCompanyResponse(
    Guid CompanyId,
    string CompanyName,
    IReadOnlyList<OperationalBranchResponse> Branches);

public sealed record OperationalBranchResponse(Guid BranchId, string BranchName);
