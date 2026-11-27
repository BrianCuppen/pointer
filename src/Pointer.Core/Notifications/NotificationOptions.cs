namespace Pointer.Core.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Public base URL for links in emails, for example https://pointer.be. No trailing slash.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5137";

    /// <summary>Attempts before a message is marked failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Local time when the owner's daily overview goes out.</summary>
    public TimeOnly DailyOverviewAt { get; set; } = new(7, 0);

    public string BookingPageUrl(string slug) => $"{PublicBaseUrl}/{slug}";

    public string CancelUrl(string cancelToken) => $"{PublicBaseUrl}/annuleren/{cancelToken}";
}
