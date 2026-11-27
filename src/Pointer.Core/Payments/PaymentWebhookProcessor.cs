using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pointer.Core.Bookings;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Notifications;

namespace Pointer.Core.Payments;

public enum WebhookOutcome
{
    Applied,

    /// <summary>This event id was processed before. Nothing changed.</summary>
    Duplicate,

    /// <summary>Recorded, but it changed nothing (unknown payment, or a stale event arriving late).</summary>
    Ignored,
}

/// <summary>
/// Applies payment events to payments and bookings. Two guarantees (hard rule 4):
/// <list type="bullet">
/// <item>Idempotent: the event id is recorded in the same transaction as its effects, so a redelivery is a no-op.</item>
/// <item>Order-safe: statuses only move forward (Pending → Failed → Succeeded → RefundPending → Refunded),
/// so a late "expired" can never undo a payment that already succeeded.</item>
/// </list>
/// </summary>
public sealed class PaymentWebhookProcessor(
    PointerDbContext db,
    IPaymentGateway gateway,
    NotificationOutbox notifications,
    TimeProvider clock,
    ILogger<PaymentWebhookProcessor> logger)
{
    public async Task<WebhookOutcome> ProcessAsync(PaymentEvent evt, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_webhook_events (provider, event_id, type, received_utc)
            VALUES ({evt.Provider}, {evt.EventId}, {evt.Type}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            await tx.RollbackAsync(ct);
            return WebhookOutcome.Duplicate;
        }

        // Lock the payment row so two different events for one payment are applied one after the other.
        var payment = await LockPaymentAsync(evt, ct);
        if (payment is null)
        {
            logger.LogWarning("Payment event {EventId} ({Type}) matches no payment", evt.EventId, evt.Type);
            await tx.CommitAsync(ct);
            return WebhookOutcome.Ignored;
        }

        // Lock the booking too: a concurrent booking request may be releasing this expired hold right now.
        var booking = await db.Bookings
            .FromSqlInterpolated($"SELECT * FROM bookings WHERE id = {payment.BookingId} FOR UPDATE")
            .SingleAsync(ct);
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == booking.ShopId, ct);
        if (evt.ConnectedAccountId is not null && evt.ConnectedAccountId != shop.PaymentAccountId)
        {
            logger.LogError("Payment event {EventId} came from account {Account}, which does not own payment {PaymentId}",
                evt.EventId, evt.ConnectedAccountId, payment.Id);
            await tx.CommitAsync(ct);
            return WebhookOutcome.Ignored;
        }

        var changed = evt.Kind switch
        {
            PaymentEventKind.CheckoutPaid => await ApplyPaidAsync(evt, payment, booking, shop, now, ct),
            PaymentEventKind.CheckoutFailed => await ApplyFailedAsync(payment, booking, now, ct),
            PaymentEventKind.Refunded => ApplyRefunded(payment, now),
            _ => false,
        };

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return changed ? WebhookOutcome.Applied : WebhookOutcome.Ignored;
    }

    private async Task<Payment?> LockPaymentAsync(PaymentEvent evt, CancellationToken ct)
    {
        if (evt.ProviderPaymentId is not null)
        {
            return await db.Payments
                .FromSqlInterpolated($"SELECT * FROM payments WHERE provider = {evt.Provider} AND provider_payment_id = {evt.ProviderPaymentId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        }

        if (evt.PaymentIntentId is not null)
        {
            return await db.Payments
                .FromSqlInterpolated($"SELECT * FROM payments WHERE provider = {evt.Provider} AND payment_intent_id = {evt.PaymentIntentId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        }

        return null;
    }

    private async Task<bool> ApplyPaidAsync(PaymentEvent evt, Payment payment, Booking booking, Shop shop, DateTime now, CancellationToken ct)
    {
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
            return false;

        payment.Status = PaymentStatus.Succeeded;
        payment.PaymentIntentId ??= evt.PaymentIntentId;
        payment.CheckoutUrl = null;
        payment.UpdatedUtc = now;

        switch (booking.Status)
        {
            case BookingStatus.PendingPayment:
                Confirm(booking);
                await notifications.OnConfirmedAsync(shop, booking, ct);
                return true;

            case BookingStatus.Cancelled:
                // Paid after the hold was released (or after a failure event that arrived first).
                // Take the slot back if it is still free; the overlap constraint decides.
                await db.SaveChangesAsync(ct);
                return await ReclaimOrRefundAsync(payment, booking, shop, now, ct);

            default:
                return true;
        }
    }

    private async Task<bool> ReclaimOrRefundAsync(Payment payment, Booking booking, Shop shop, DateTime now, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction!;
        await tx.CreateSavepointAsync("reclaim", ct);
        Confirm(booking);
        booking.CancelledUtc = null;
        try
        {
            await db.SaveChangesAsync(ct);
            await notifications.OnConfirmedAsync(shop, booking, ct);
            logger.LogInformation("Booking {BookingId} paid after its hold was released; slot was still free and is confirmed", booking.Id);
            return true;
        }
        catch (DbUpdateException ex) when (BookingService.IsOverlapViolation(ex))
        {
            await tx.RollbackToSavepointAsync("reclaim", ct);
            await db.Entry(booking).ReloadAsync(ct); // back to cancelled
        }

        // Someone else has the slot now. The customer must get their money back.
        if (payment.PaymentIntentId is null || string.IsNullOrEmpty(shop.PaymentAccountId))
            throw new InvalidOperationException($"Cannot refund payment {payment.Id}: missing payment intent or connected account.");

        // Inside the transaction on purpose: if the refund call fails, nothing is committed, the event is
        // not marked processed, and the provider redelivers it. The idempotency key makes the retry safe.
        await gateway.RefundAsync(shop.PaymentAccountId, payment.PaymentIntentId, $"refund-slot-taken-{payment.Id}", ct);
        payment.Status = PaymentStatus.RefundPending;
        payment.UpdatedUtc = now;
        await notifications.OnPaymentFailedAsync(booking, ct);
        logger.LogWarning("Booking {BookingId} paid after its slot was taken by another booking; deposit refunded", booking.Id);
        return true;
    }

    private async Task<bool> ApplyFailedAsync(Payment payment, Booking booking, DateTime now, CancellationToken ct)
    {
        if (payment.Status != PaymentStatus.Pending)
            return false;

        payment.Status = PaymentStatus.Failed;
        payment.CheckoutUrl = null;
        payment.UpdatedUtc = now;

        if (booking.Status == BookingStatus.PendingPayment)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.HoldExpiresUtc = null;
            booking.CancelledUtc = now;
        }
        // Also when a booking request already released the expired hold.
        if (booking.Status == BookingStatus.Cancelled)
            await notifications.OnPaymentFailedAsync(booking, ct);
        return true;
    }

    private static bool ApplyRefunded(Payment payment, DateTime now)
    {
        if (payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.RefundPending))
            return false;

        payment.Status = PaymentStatus.Refunded;
        payment.UpdatedUtc = now;
        return true;
    }

    private static void Confirm(Booking booking)
    {
        booking.Status = BookingStatus.Confirmed;
        booking.HoldExpiresUtc = null;
    }
}
