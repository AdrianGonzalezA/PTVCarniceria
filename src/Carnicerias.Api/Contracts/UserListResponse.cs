namespace Carnicerias.Api.Contracts;

public sealed record UserListResponse(
    IReadOnlyList<UserListItemResponse> Items,
    int Page,
    int PageSize,
    long TotalItems,
    long TotalPages);

public sealed record UserListItemResponse(
    Guid UserId,
    string Username,
    string Email,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
