namespace Services.Database;

public sealed class McpUsageDocument
{
    public string Id { get; set; } = "usage";

    public string UserId { get; set; } = "";

    public string UtcDay { get; set; } = "";

    public int DailyCount
    {
        get; set;
    }

    public List<DateTimeOffset> RecentAdmissions { get; set; } = [];

    public int Ttl { get; set; } = 172800;

    public string? ETag
    {
        get; set;
    }
}
