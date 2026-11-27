using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Pointer.Core.Notifications;

namespace Pointer.Web.Notifications;

/// <summary>
/// Brevo transactional email API (EU company, EU data storage). Plain HttpClient, no SDK:
/// one endpoint does not justify a dependency.
/// </summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public const string BaseAddress = "https://api.brevo.com/";

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = o.FromName, email = o.FromAddress },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                textContent = message.TextBody,
                attachment = message.Attachments.Count == 0
                    ? null
                    : message.Attachments.Select(a => new { name = a.FileName, content = Convert.ToBase64String(a.Content) }).ToArray(),
            }, options: new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
        };
        request.Headers.Add("api-key", o.BrevoApiKey);
        request.Headers.Add("accept", "application/json");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Used when no Brevo key is configured: logs that an email would be sent, never its content.</summary>
public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Email not sent (no Brevo key configured): \"{Subject}\"", message.Subject);
        return Task.CompletedTask;
    }
}
