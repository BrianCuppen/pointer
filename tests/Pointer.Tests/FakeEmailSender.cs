using Pointer.Core.Notifications;

namespace Pointer.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public int FailNext { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new HttpRequestException("Brevo unavailable");
        }
        lock (Sent)
            Sent.Add(message);
        return Task.CompletedTask;
    }
}
