namespace Services.Database;

public sealed class UserDocument
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string Email { get; set; } = "";

    public string NormalisedEmail { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
