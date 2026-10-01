namespace Services.Database;

public sealed class StripeEventDocument
{
    public string Id { get; set; } = "";
    public string PartitionId { get; set; } = "stripe";
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
