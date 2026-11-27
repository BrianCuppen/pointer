using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Notifications;

namespace Pointer.Core.Payments;

public abstract record StartCheckoutResult
{
    private StartCheckoutResult() { }

    /// <summary>Send the customer to <paramref name="Url"/> to pay the deposit.</summary>
    public sealed record Redirect(string Url) : StartCheckoutResult;

    /// <summary>The service has no deposit: the booking is confirmed without payment.</summary>
    public sealed record ConfirmedWithoutDeposit : StartCheckoutResult;

    public sealed record Invalid(string Reason) : StartCheckoutResult;
}

/// <summary>Starts the deposit payment for a booking that holds a slot.</summary>
public sealed class DepositService(
    PointerDbContext db,
    IPaymentGateway gateway,
    NotificationOutbox notifications,
    TimeProvider clock,
    IOptions<PlatformFeeOptions> fees)
{
    /// <summary>Stripe Checkout sessions must stay open at least 30 minutes.</summary>
    internal static readonly TimeSpan MinimumCheckoutLifetime = TimeSpan.FromMinutes(31);

    public async Task<StartCheckoutResult> StartCheckoutAsync(Guid bookingId, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var booking = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
            return new StartCheckoutResult.Invalid("booking_not_found");
        if (booking.Status != BookingStatus.PendingPayment)
            return new StartCheckoutResult.Invalid("booking_not_pending");
        if (booking.HoldExpiresUtc is null || booking.HoldExpiresUtc <= now)
            return new StartCheckoutResult.Invalid("hold_expired");

        if (booking.DepositCents == 0)
        {
            var owner = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == booking.ShopId, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            booking.Status = BookingStatus.Confirmed;
            booking.HoldExpiresUtc = null;
            await db.SaveChangesAsync(ct);
            await notifications.OnConfirmedAsync(owner, booking, ct);
            await tx.CommitAsync(ct);
            return new StartCheckoutResult.ConfirmedWithoutDeposit();
        }

        var open = booking.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Pending && p.CheckoutUrl is not null);
        if (open is not null)
            return new StartCheckoutResult.Redirect(open.CheckoutUrl!);

        var shop = await db.Shops.AsNoTracking().FirstAsync(s => s.Id == booking.ShopId, ct);
        if (string.IsNullOrEmpty(shop.PaymentAccountId))
            return new StartCheckoutResult.Invalid("shop_not_connected");

        // The checkout must close no later than the slot hold, so nobody can pay for a released slot
        // in the normal flow. Stripe needs at least 30 minutes, so extend the hold if needed.
        var minimumExpiry = now + MinimumCheckoutLifetime;
        if (booking.HoldExpiresUtc < minimumExpiry)
            booking.HoldExpiresUtc = minimumExpiry;

        var fee = fees.Value.FeeFor(booking.DepositCents);
        var checkout = await gateway.CreateDepositCheckoutAsync(new DepositCheckoutRequest(
            ConnectedAccountId: shop.PaymentAccountId,
            BookingId: booking.Id,
            AmountCents: booking.DepositCents,
            ApplicationFeeCents: fee,
            Currency: "EUR",
            Description: booking.Service!.Name,
            CustomerEmail: booking.CustomerEmail,
            ExpiresUtc: booking.HoldExpiresUtc.Value,
            SuccessUrl: successUrl,
            CancelUrl: cancelUrl,
            // Same booking, same checkout: a retried request cannot create a second session.
            IdempotencyKey: $"deposit-checkout-{booking.Id}"), ct);

        db.Payments.Add(new Payment
        {
            BookingId = booking.Id,
            Provider = gateway.Provider,
            ProviderPaymentId = checkout.SessionId,
            CheckoutUrl = checkout.Url,
            AmountCents = booking.DepositCents,
            PlatformFeeCents = fee,
            Currency = "EUR",
            Status = PaymentStatus.Pending,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await db.SaveChangesAsync(ct);

        return new StartCheckoutResult.Redirect(checkout.Url);
    }
}
