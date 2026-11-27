using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Notifications;
using Pointer.Core.Payments;

namespace Pointer.Core.Bookings;

public enum LifecycleResult
{
    Done,
    NotFound,

    /// <summary>The booking is not in a state that allows this (already cancelled, in the past, not started yet...).</summary>
    NotAllowed,
}

/// <summary>
/// Cancellations, no-shows and completions, with the deposit rules applied (hard rule 5):
/// <list type="bullet">
/// <item>Customer cancels inside the free window: deposit refunded. After it: the shop keeps it.</item>
/// <item>Shop cancels: deposit always refunded.</item>
/// <item>No-show: refunded only if the shop's policy (snapshotted on the booking) says so.</item>
/// </list>
/// Refunds are requested inside the transaction with an idempotency key, like the webhook handler:
/// if the provider call fails nothing changes, and a retry cannot refund twice.
/// </summary>
public sealed class BookingLifecycleService(
    PointerDbContext db,
    IPaymentGateway gateway,
    NotificationOutbox notifications,
    TimeProvider clock,
    ILogger<BookingLifecycleService> logger)
{
    /// <summary>What a customer would get if they cancelled now. For showing before they confirm.</summary>
    public bool WouldRefundCustomerCancellation(Booking booking) =>
        clock.GetUtcNow().UtcDateTime < FreeCancellationEnd(booking);

    public static DateTime FreeCancellationEnd(Booking booking) => booking.StartUtc.AddMinutes(-booking.FreeCancellationMinutes);

    public Task<Booking?> FindByCancelTokenAsync(string token, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking().Include(b => b.Service).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.CancelToken == token, ct);

    public async Task<LifecycleResult> CancelByCustomerAsync(string cancelToken, CancellationToken ct = default)
    {
        var id = await db.Bookings.Where(b => b.CancelToken == cancelToken).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        return id is null ? LifecycleResult.NotFound : await CancelAsync(id.Value, shopId: null, byCustomer: true, ct);
    }

    public Task<LifecycleResult> CancelByShopAsync(Guid shopId, Guid bookingId, CancellationToken ct = default) =>
        CancelAsync(bookingId, shopId, byCustomer: false, ct);

    public async Task<LifecycleResult> MarkNoShowAsync(Guid shopId, Guid bookingId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (booking, shop) = await LockAsync(bookingId, shopId, ct);
        if (booking is null) return LifecycleResult.NotFound;
        if (booking.Status != BookingStatus.Confirmed || booking.StartUtc > now) return LifecycleResult.NotAllowed;

        booking.Status = BookingStatus.NoShow;
        if (booking.NoShowPolicy == NoShowPolicy.RefundDeposit)
            await RefundAsync(shop!, booking, "no-show", now, ct);

        await db.SaveChangesAsync(ct);
        await notifications.OnNoShowAsync(shop!, booking, ct);
        await tx.CommitAsync(ct);
        return LifecycleResult.Done;
    }

    public async Task<LifecycleResult> MarkCompletedAsync(Guid shopId, Guid bookingId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (booking, _) = await LockAsync(bookingId, shopId, ct);
        if (booking is null) return LifecycleResult.NotFound;
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.NoShow) || booking.StartUtc > now) return LifecycleResult.NotAllowed;
        if (booking.Status == BookingStatus.NoShow && booking.Payments.Any(p => p.Status is PaymentStatus.RefundPending or PaymentStatus.Refunded))
            return LifecycleResult.NotAllowed; // the refund already went out; keep the record honest

        booking.Status = BookingStatus.Completed;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return LifecycleResult.Done;
    }

    private async Task<LifecycleResult> CancelAsync(Guid bookingId, Guid? shopId, bool byCustomer, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (booking, shop) = await LockAsync(bookingId, shopId, ct);
        if (booking is null) return LifecycleResult.NotFound;
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.PendingPayment) || booking.StartUtc <= now)
            return LifecycleResult.NotAllowed;

        var refund = !byCustomer || now < FreeCancellationEnd(booking);
        booking.Status = BookingStatus.Cancelled;
        booking.HoldExpiresUtc = null;
        booking.CancelledUtc = now;
        if (refund)
            await RefundAsync(shop!, booking, byCustomer ? "customer-cancel" : "shop-cancel", now, ct);

        await db.SaveChangesAsync(ct);
        await notifications.OnCancelledAsync(shop!, booking, byCustomer, ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Booking {BookingId} cancelled by {Who}, refund: {Refund}", booking.Id, byCustomer ? "customer" : "shop", refund);
        return LifecycleResult.Done;
    }

    private async Task RefundAsync(Shop shop, Booking booking, string reason, DateTime now, CancellationToken ct)
    {
        foreach (var payment in booking.Payments.Where(p => p.Status == PaymentStatus.Succeeded))
        {
            if (payment.PaymentIntentId is null || string.IsNullOrEmpty(shop.PaymentAccountId))
                throw new InvalidOperationException($"Cannot refund payment {payment.Id}: missing payment intent or connected account.");
            await gateway.RefundAsync(shop.PaymentAccountId, payment.PaymentIntentId, $"refund-{reason}-{payment.Id}", ct);
            payment.Status = PaymentStatus.RefundPending;
            payment.UpdatedUtc = now;
        }
    }

    /// <summary>Locks the booking (and with it, its payments' fate) for the rest of the transaction.</summary>
    private async Task<(Booking? Booking, Shop? Shop)> LockAsync(Guid bookingId, Guid? shopId, CancellationToken ct)
    {
        var booking = await db.Bookings
            .FromSqlInterpolated($"SELECT * FROM bookings WHERE id = {bookingId} FOR UPDATE")
            .Include(b => b.Payments)
            .SingleOrDefaultAsync(ct);
        if (booking is null || (shopId is not null && booking.ShopId != shopId))
            return (null, null);
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == booking.ShopId, ct);
        return (booking, shop);
    }
}
