namespace Carnicerias.Api.Contracts;

public sealed record ErrorResponse(ApiError Error);

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyList<object> Details);
