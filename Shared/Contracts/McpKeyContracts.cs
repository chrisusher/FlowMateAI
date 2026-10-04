namespace Shared.Contracts;

public sealed record McpKeyCreateRequest(string Name, int ExpiryDays = 90);

public sealed record McpKeyCreatedResponse(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Key);

public sealed record McpKeySummary(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked);
