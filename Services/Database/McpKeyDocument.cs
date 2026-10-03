namespace Services.Database;

public sealed class McpKeyDocument
{
    [Newtonsoft.Json.JsonProperty("id")]
    public string Id { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("userId")]
    public string UserId { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("name")]
    public string Name { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("digest")]
    public string Digest { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("vaultSecretName")]
    public string VaultSecretName { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("prefix")]
    public string Prefix { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Newtonsoft.Json.JsonProperty("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Newtonsoft.Json.JsonProperty("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? ETag { get; set; }
}
