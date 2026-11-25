namespace Shared.Contracts;

public sealed record ProjectCreateRequest(string? Id, string? Name, string? Color, DateTimeOffset? CreatedAt);
