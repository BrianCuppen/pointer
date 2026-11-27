using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Notifications;

/// <summary>
/// Decides which emails a booking change causes and queues them in the current transaction.
/// Queuing the same message twice for one booking is a no-op (unique index + ON CONFLICT DO NOTHING),
/// so a redelivered webhook can never send a second confirmation.
/// </summary>
public sealed class NotificationOutbox(PointerDbContext db, TimeProvider clock)
{
    public async Task OnConfirmedAsync(Shop shop, Booking booking, CancellationToken ct = default)
    {
        await EnqueueAsync(booking, NotificationType.CustomerConfirmation, ct);
        if (OwnerWantsInstant(shop))
            await EnqueueAsync(booking, NotificationType.OwnerNewBooking, ct);
    }

    /// <summary>Payment failed, checkout expired, or the slot was gone when the payment arrived.</summary>
    public Task OnPaymentFailedAsync(Booking booking, CancellationToken ct = default) =>
        EnqueueAsync(booking, NotificationType.CustomerPaymentFailed, ct);

    public async Task OnCancelledAsync(Shop shop, Booking booking, bool byCustomer, CancellationToken ct = default)
    {
        await EnqueueAsync(booking, NotificationType.CustomerCancellation, ct);
        if (byCustomer && OwnerWantsInstant(shop))
            await EnqueueAsync(booking, NotificationType.OwnerCancellation, ct);
    }

    public async Task OnNoShowAsync(Shop shop, Booking booking, CancellationToken ct = default)
    {
        if (shop.NoShowNoticeEnabled)
            await EnqueueAsync(booking, NotificationType.CustomerNoShow, ct);
    }

    private static bool OwnerWantsInstant(Shop shop) => shop.OwnerInstantEmails && !string.IsNullOrWhiteSpace(shop.OwnerEmail);

    private async Task EnqueueAsync(Booking booking, NotificationType type, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications (id, shop_id, booking_id, type, status, attempts, created_utc)
            VALUES ({Guid.NewGuid()}, {booking.ShopId}, {booking.Id}, {Snake(type)}, 'pending', 0, {now})
            ON CONFLICT DO NOTHING
            """, ct);
    }

    internal static string Snake(NotificationType type) => SnakeCaseEnumConverter<NotificationType>.ToSnakeCase(type.ToString());
}
