using Microsoft.EntityFrameworkCore;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BookingLifecycleTests(PostgresFixture fixture) : DepositTestBase(fixture)
{
    /// <summary>A paid, confirmed booking at <see cref="DepositTestBase.TenAm"/> (2 Dec). Free cancellation: 24 hours.</summary>
    private async Task<(SeededShop Shop, Booking Booking)> PaidBookingAsync(Action<Shop>? configure = null)
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        configure?.Invoke(s.Shop);
        await db.SaveChangesAsync();
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await Webhooks(db).ProcessAsync(Paid(session, $"evt_{session}"));
        return (s, booking);
    }

    private async Task<(BookingStatus Booking, PaymentStatus Payment, List<NotificationType> Mails)> StateAsync(Guid bookingId)
    {
        await using var db = Fixture.CreateContext();
        var b = await db.Bookings.AsNoTracking().Include(x => x.Payments).SingleAsync(x => x.Id == bookingId);
        var mails = await db.Notifications.AsNoTracking().Where(n => n.BookingId == bookingId).Select(n => n.Type).ToListAsync();
        return (b.Status, b.Payments.Single().Status, mails);
    }

    private async Task<LifecycleResult> CustomerCancelsAsync(Booking booking)
    {
        await using var db = Fixture.CreateContext();
        return await Lifecycle(db).CancelByCustomerAsync(booking.CancelToken);
    }

    [Fact]
    public async Task Customer_cancelling_in_time_gets_the_deposit_back()
    {
        var (_, booking) = await PaidBookingAsync();
        Clock.Now = new DateTimeOffset(TenAm.AddHours(-25), TimeSpan.Zero);

        Assert.Equal(LifecycleResult.Done, await CustomerCancelsAsync(booking));

        var (status, payment, mails) = await StateAsync(booking.Id);
        Assert.Equal((BookingStatus.Cancelled, PaymentStatus.RefundPending), (status, payment));
        Assert.Equal($"refund-customer-cancel-", Gateway.Refunds.Single().IdempotencyKey[..23]);
        Assert.Contains(NotificationType.CustomerCancellation, mails);
    }

    [Fact]
    public async Task Customer_cancelling_late_does_not_get_the_deposit_back()
    {
        var (_, booking) = await PaidBookingAsync();
        Clock.Now = new DateTimeOffset(TenAm.AddHours(-23), TimeSpan.Zero);

        Assert.Equal(LifecycleResult.Done, await CustomerCancelsAsync(booking));

        var (status, payment, _) = await StateAsync(booking.Id);
        Assert.Equal((BookingStatus.Cancelled, PaymentStatus.Succeeded), (status, payment));
        Assert.Empty(Gateway.Refunds);
    }

    [Fact]
    public async Task Cancelling_twice_or_after_the_start_is_not_allowed()
    {
        var (_, booking) = await PaidBookingAsync();
        Clock.Now = new DateTimeOffset(TenAm.AddHours(-25), TimeSpan.Zero);
        await CustomerCancelsAsync(booking);

        Assert.Equal(LifecycleResult.NotAllowed, await CustomerCancelsAsync(booking));
        Assert.Single(Gateway.Refunds);

        var (_, other) = await PaidBookingAsync();
        Clock.Now = new DateTimeOffset(TenAm.AddMinutes(1), TimeSpan.Zero);
        Assert.Equal(LifecycleResult.NotAllowed, await CustomerCancelsAsync(other));
        Assert.Equal(LifecycleResult.NotFound, await CustomerCancelsAsync(new Booking { CustomerName = "", CustomerEmail = "" }));
    }

    [Fact]
    public async Task Shop_cancelling_always_refunds_and_owner_gets_no_cancellation_email()
    {
        var (s, booking) = await PaidBookingAsync(shop => shop.OwnerEmail = "owner@example.com");
        Clock.Now = new DateTimeOffset(TenAm.AddHours(-1), TimeSpan.Zero);

        await using (var db = Fixture.CreateContext())
            Assert.Equal(LifecycleResult.Done, await Lifecycle(db).CancelByShopAsync(s.Shop.Id, booking.Id));

        var (status, payment, mails) = await StateAsync(booking.Id);
        Assert.Equal((BookingStatus.Cancelled, PaymentStatus.RefundPending), (status, payment));
        Assert.Contains(NotificationType.CustomerCancellation, mails);
        Assert.DoesNotContain(NotificationType.OwnerCancellation, mails);
    }

    [Fact]
    public async Task Another_shop_cannot_cancel_the_booking()
    {
        var (_, booking) = await PaidBookingAsync();
        await using var db = Fixture.CreateContext();
        var other = await TestData.SeedShopAsync(db);

        Assert.Equal(LifecycleResult.NotFound, await Lifecycle(db).CancelByShopAsync(other.Shop.Id, booking.Id));
        Assert.Equal(BookingStatus.Confirmed, (await StateAsync(booking.Id)).Booking);
    }

    [Theory]
    [InlineData(NoShowPolicy.KeepDeposit, PaymentStatus.Succeeded)]
    [InlineData(NoShowPolicy.RefundDeposit, PaymentStatus.RefundPending)]
    public async Task No_show_follows_the_policy_shown_at_booking(NoShowPolicy policy, PaymentStatus expected)
    {
        var (s, booking) = await PaidBookingAsync(shop => shop.NoShowPolicy = policy);
        await using (var db = Fixture.CreateContext())
        {
            // Changing the policy later must not change this booking's deal.
            var shop = await db.Shops.SingleAsync(x => x.Id == s.Shop.Id);
            shop.NoShowPolicy = policy == NoShowPolicy.KeepDeposit ? NoShowPolicy.RefundDeposit : NoShowPolicy.KeepDeposit;
            await db.SaveChangesAsync();
        }

        await using var db2 = Fixture.CreateContext();
        Clock.Now = new DateTimeOffset(TenAm.AddMinutes(-5), TimeSpan.Zero);
        Assert.Equal(LifecycleResult.NotAllowed, await Lifecycle(db2).MarkNoShowAsync(s.Shop.Id, booking.Id)); // not started yet
        Clock.Now = new DateTimeOffset(TenAm.AddMinutes(20), TimeSpan.Zero);
        Assert.Equal(LifecycleResult.Done, await Lifecycle(db2).MarkNoShowAsync(s.Shop.Id, booking.Id));

        var (status, payment, mails) = await StateAsync(booking.Id);
        Assert.Equal((BookingStatus.NoShow, expected), (status, payment));
        Assert.Contains(NotificationType.CustomerNoShow, mails);
    }

    [Fact]
    public async Task Completed_after_the_start_only()
    {
        var (s, booking) = await PaidBookingAsync();
        await using var db = Fixture.CreateContext();

        Assert.Equal(LifecycleResult.NotAllowed, await Lifecycle(db).MarkCompletedAsync(s.Shop.Id, booking.Id));
        Clock.Now = new DateTimeOffset(TenAm.AddMinutes(40), TimeSpan.Zero);
        Assert.Equal(LifecycleResult.Done, await Lifecycle(db).MarkCompletedAsync(s.Shop.Id, booking.Id));
        Assert.Equal(BookingStatus.Completed, (await StateAsync(booking.Id)).Booking);
    }
}
