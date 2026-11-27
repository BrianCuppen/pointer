namespace Pointer.Core.Domain;

/// <summary>
/// One row per provider event that has been applied. Inserted in the same transaction as the
/// state change it caused, so a redelivered event is detected and skipped (hard rule 4).
/// </summary>
public class ProcessedWebhookEvent
{
    public required string Provider { get; set; }
    public required string EventId { get; set; }
    public required string Type { get; set; }
    public DateTime ReceivedUtc { get; set; }
}
