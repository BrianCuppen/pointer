namespace Pointer.Core.Notifications;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(string To, string Subject, string TextBody, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Sends one email. Implementations: Brevo in production, a fake in tests.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
