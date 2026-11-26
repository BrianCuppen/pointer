using Microsoft.EntityFrameworkCore;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PaymentWebhookProcessorTests(PostgresFixture fixture) : DepositTestBase(fixture)
{
    private async Task<(Booking Booking, Payment Payment)> LoadAsync(Guid bookingId)
    {
        await using var db = Fixture.CreateContext();
        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == bookingId);
        return (booking, payment);
    }

    private async Task<WebhookOutcome> ProcessAsync(PaymentEvent evt)
    {
        await using var db = Fixture.CreateContext();
        return await Webhooks(db).ProcessAsync(evt);
    }

    [Fact]
    public async Task Paid_confirms_the_booking()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(Paid(session, $"evt_{session}")));

        var (b, p) = await LoadAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Null(b.HoldExpiresUtc);
        Assert.Equal(PaymentStatus.Succeeded, p.Status);
        Assert.Equal($"pi_{session}", p.PaymentIntentId);
        Assert.Null(p.CheckoutUrl);
    }

    [Fact]
    public async Task Same_event_delivered_twice_is_applied_once()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        var evt = Paid(session, $"evt_{session}");

        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(evt));
        Assert.Equal(WebhookOutcome.Duplicate, await ProcessAsync(evt));

        Assert.Equal(BookingStatus.Confirmed, (await LoadAsync(booking.Id)).Booking.Status);
    }

    [Fact]
    public async Task Same_event_delivered_concurrently_is_applied_once()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        var evt = Paid(session, $"evt_{session}");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ProcessAsync(evt))));

        Assert.Equal(1, outcomes.Count(o => o == WebhookOutcome.Applied));
        Assert.Equal(7, outcomes.Count(o => o == WebhookOutcome.Duplicate));
    }

    [Fact]
    public async Task Late_expired_event_cannot_undo_a_payment()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        await ProcessAsync(Paid(session, $"evt_paid_{session}"));
        Assert.Equal(WebhookOutcome.Ignored, await ProcessAsync(Expired(session, $"evt_exp_{session}")));

        var (b, p) = await LoadAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(PaymentStatus.Succeeded, p.Status);
    }

    [Fact]
    public async Task Expired_checkout_releases_the_slot()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(Expired(session, $"evt_exp_{session}")));

        var (b, p) = await LoadAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(PaymentStatus.Failed, p.Status);
        await using var db2 = Fixture.CreateContext();
        await HoldAsync(db2, s, TenAm, "someone@example.com"); // slot is free again
    }

    [Fact]
    public async Task Paid_after_release_reclaims_a_slot_that_is_still_free()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        await ProcessAsync(Expired(session, $"evt_exp_{session}"));          // arrives first (out of order)
        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(Paid(session, $"evt_paid_{session}")));

        var (b, p) = await LoadAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Null(b.CancelledUtc);
        Assert.Equal(PaymentStatus.Succeeded, p.Status);
        Assert.Empty(Gateway.Refunds);
    }

    [Fact]
    public async Task Paid_after_slot_was_taken_refunds_the_customer()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await ProcessAsync(Expired(session, $"evt_exp_{session}"));
        await using (var other = Fixture.CreateContext())
            await HoldAsync(other, s, TenAm, "someone@example.com");          // somebody else takes the slot

        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(Paid(session, $"evt_paid_{session}")));

        var (b, p) = await LoadAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(PaymentStatus.RefundPending, p.Status);
        var refund = Assert.Single(Gateway.Refunds);
        Assert.Equal((Account, $"pi_{session}", $"refund-slot-taken-{p.Id}"), refund);

        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(Refunded(session, $"evt_ref_{session}")));
        Assert.Equal(PaymentStatus.Refunded, (await LoadAsync(booking.Id)).Payment.Status);
    }

    [Fact]
    public async Task Failed_refund_call_is_not_recorded_so_the_retry_refunds()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await ProcessAsync(Expired(session, $"evt_exp_{session}"));
        await using (var other = Fixture.CreateContext())
            await HoldAsync(other, s, TenAm, "someone@example.com");
        var paid = Paid(session, $"evt_paid_{session}");

        Gateway.FailRefunds = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => ProcessAsync(paid));
        Assert.Equal(PaymentStatus.Failed, (await LoadAsync(booking.Id)).Payment.Status);

        Gateway.FailRefunds = false;
        Assert.Equal(WebhookOutcome.Applied, await ProcessAsync(paid));
        Assert.Single(Gateway.Refunds);
        Assert.Equal(PaymentStatus.RefundPending, (await LoadAsync(booking.Id)).Payment.Status);
    }

    [Fact]
    public async Task Event_from_another_connected_account_changes_nothing()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        Assert.Equal(WebhookOutcome.Ignored, await ProcessAsync(Paid(session, $"evt_{session}", account: "acct_attacker")));

        Assert.Equal(BookingStatus.PendingPayment, (await LoadAsync(booking.Id)).Booking.Status);
    }

    [Fact]
    public async Task Unknown_payment_is_ignored()
    {
        Assert.Equal(WebhookOutcome.Ignored, await ProcessAsync(Paid($"cs_unknown_{Guid.NewGuid():N}", $"evt_{Guid.NewGuid():N}")));
    }
}
