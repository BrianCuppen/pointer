namespace Pointer.Web.Notifications;

/// <summary>Email__BrevoApiKey comes from the environment. Without it, emails are only logged (demo and development).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? BrevoApiKey { get; set; }
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "Pointer";
}
