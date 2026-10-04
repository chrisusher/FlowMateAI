namespace Web.Clients;

public sealed class AuthSession
{
    public bool Configured { get; set; }
    public bool SignedIn { get; set; }
    public string? Sub { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? AccessToken { get; set; }
    public long? ExpiresAt { get; set; }
    public bool SessionExpired { get; set; }
}
