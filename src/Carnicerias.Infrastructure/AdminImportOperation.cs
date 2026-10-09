namespace Carnicerias.Infrastructure;

public sealed class AdminImportOperation
{
    private AdminImportOperation()
    {
        Kind = string.Empty;
        RequestHash = string.Empty;
        ResultJson = string.Empty;
    }

    public AdminImportOperation(Guid companyId, Guid operationId, Guid userId, string kind,
        string requestHash, string resultJson, DateTimeOffset createdAtUtc)
    {
        if (companyId == Guid.Empty || operationId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Company, operation and user ids are required.");
        if (string.IsNullOrWhiteSpace(kind) || requestHash.Length != 64 || string.IsNullOrWhiteSpace(resultJson))
            throw new ArgumentException("Import metadata is invalid.");
        CompanyId = companyId;
        OperationId = operationId;
        UserId = userId;
        Kind = kind;
        RequestHash = requestHash;
        ResultJson = resultJson;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid CompanyId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Kind { get; private set; }
    public string RequestHash { get; private set; }
    public string ResultJson { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
