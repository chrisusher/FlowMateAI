namespace Services.Database;

public sealed class McpKeyDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("userId")]
    public string UserId { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("digest")]
    public string Digest { get; set; } = "";

    [JsonProperty("vaultSecretName")]
    public string VaultSecretName { get; set; } = "";

    [JsonProperty("prefix")]
    public string Prefix { get; set; } = "";

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonProperty("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonProperty("_etag")]
    public string? ETag { get; set; }
}
