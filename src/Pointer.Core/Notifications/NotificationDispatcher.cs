using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Notifications;

/// <summary>
/// Sends queued notifications. Rows are claimed with FOR UPDATE SKIP LOCKED, so several app
/// instances never pick the same row, and moved to "sending" before the provider call: if the
/// process dies mid-send the row stays there and is not sent again (at most once).
/// </summary>
public sealed class NotificationDispatcher(
    PointerDbContext db,
    NotificationRenderer renderer,
    IEmailSender sender,
    TimeProvider clock,
    IOptions<NotificationOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    public async Task<int> DispatchDueAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<Notification> claimed;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            claimed = await db.Notifications
                .FromSqlInterpolated($"""
                    SELECT * FROM notifications
                    WHERE status = 'pending' AND (not_before_utc IS NULL OR not_before_utc <= {now})
                    ORDER BY created_utc
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);
            foreach (var n in claimed)
            {
                n.Status = NotificationStatus.Sending;
                n.Attempts++;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        foreach (var n in claimed)
            await SendOneAsync(n, ct);
        return claimed.Count;
    }

    private async Task SendOneAsync(Notification n, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var message = await renderer.RenderAsync(n, ct);
            if (message is null)
            {
                n.Status = NotificationStatus.Skipped;
            }
            else
            {
                await sender.SendAsync(message, ct);
                n.Status = NotificationStatus.Sent;
                n.Recipient = message.To;
                n.SentUtc = now;
                logger.LogInformation("Sent {Type} for booking {BookingId}", n.Type, n.BookingId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            n.LastError = ex.GetType().Name;
            if (n.Attempts >= options.Value.MaxAttempts)
            {
                n.Status = NotificationStatus.Failed;
                logger.LogError(ex, "Giving up on {Type} for booking {BookingId} after {Attempts} attempts", n.Type, n.BookingId, n.Attempts);
            }
            else
            {
                n.Status = NotificationStatus.Pending;
                n.NotBeforeUtc = now.AddMinutes(Math.Pow(2, n.Attempts)); // 2, 4, 8, 16 minutes
                logger.LogWarning(ex, "Sending {Type} for booking {BookingId} failed, will retry", n.Type, n.BookingId);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
