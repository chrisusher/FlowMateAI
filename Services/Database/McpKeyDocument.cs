namespace Services.Database;

public sealed class McpKeyDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Digest { get; set; } = "";

    public string VaultSecretName { get; set; } = "";

    public string Prefix { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? ETag { get; set; }
}
