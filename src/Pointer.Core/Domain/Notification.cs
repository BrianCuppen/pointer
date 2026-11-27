namespace Pointer.Core.Domain;

public enum NotificationType
{
    OwnerNewBooking,
    OwnerCancellation,
    OwnerDailyOverview,
    CustomerConfirmation,
    CustomerReminder,
    CustomerCancellation,
    CustomerPaymentFailed,
    CustomerNoShow,
}

public enum NotificationStatus
{
    Pending,

    /// <summary>Claimed by a dispatcher. Left here if the process dies mid-send, so it is never sent twice.</summary>
    Sending,
    Sent,

    /// <summary>No longer relevant at send time (for example a reminder for a booking cancelled since).</summary>
    Skipped,
    Failed,
}

/// <summary>
/// Outbox row: one email to send. Written in the same transaction as the booking change that causes it.
/// A unique index on (booking_id, type) makes each message happen at most once per booking.
/// The body is never stored, only what was sent to whom and when.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid? BookingId { get; set; }
    public NotificationType Type { get; set; }

    /// <summary>Local date of a daily overview. Null for booking messages.</summary>
    public DateOnly? OverviewDate { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }

    /// <summary>Earliest time to try again after a failed attempt.</summary>
    public DateTime? NotBeforeUtc { get; set; }

    /// <summary>Set when sent.</summary>
    public string? Recipient { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }

    /// <summary>Exception type of the last failure. Never message content.</summary>
    public string? LastError { get; set; }
}
