using Microsoft.EntityFrameworkCore;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DepositServiceTests(PostgresFixture fixture) : DepositTestBase(fixture)
{
    [Fact]
    public async Task Starts_checkout_on_the_shops_connected_account_with_platform_fee()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var booking = await HoldAsync(db, s, TenAm);

        var result = await Deposits(db).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        var redirect = Assert.IsType<StartCheckoutResult.Redirect>(result);
        var request = Assert.Single(Gateway.Checkouts);
        Assert.Equal(Account, request.ConnectedAccountId);
        Assert.Equal(500, request.AmountCents);
        Assert.Equal(30, request.ApplicationFeeCents);
        Assert.Equal("EUR", request.Currency);
        Assert.Equal(booking.HoldExpiresUtc, request.ExpiresUtc);
        Assert.Equal($"deposit-checkout-{booking.Id}", request.IdempotencyKey);

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == booking.Id);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(30, payment.PlatformFeeCents);
        Assert.Equal(redirect.Url, payment.CheckoutUrl);
    }

    [Fact]
    public async Task Checkout_never_outlives_the_hold_and_lasts_at_least_30_minutes()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var booking = await HoldAsync(db, s, TenAm);
        Clock.Now = Now.AddMinutes(20); // 10 minutes of hold left

        await Deposits(db).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        var request = Assert.Single(Gateway.Checkouts);
        Assert.True(request.ExpiresUtc >= Clock.Now.UtcDateTime.AddMinutes(30));
        var stored = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(request.ExpiresUtc, stored.HoldExpiresUtc);
    }

    [Fact]
    public async Task Opening_the_payment_step_twice_reuses_the_checkout()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        var booking = await HoldAsync(db, s, TenAm);
        var deposits = Deposits(db);

        var first = await deposits.StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");
        var second = await deposits.StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        Assert.Equal(first, second);
        Assert.Single(Gateway.Checkouts);
    }

    [Fact]
    public async Task Fee_is_never_more_than_the_deposit()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        s.Haircut.DepositCents = 20;
        await db.SaveChangesAsync();
        var booking = await HoldAsync(db, s, TenAm);

        await Deposits(db, feeCents: 30).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        Assert.Equal(20, Assert.Single(Gateway.Checkouts).ApplicationFeeCents);
    }

    [Fact]
    public async Task Service_without_deposit_is_confirmed_without_payment()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedConnectedShopAsync(db);
        s.Haircut.DepositCents = 0;
        await db.SaveChangesAsync();
        var booking = await HoldAsync(db, s, TenAm);

        var result = await Deposits(db).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        Assert.IsType<StartCheckoutResult.ConfirmedWithoutDeposit>(result);
        Assert.Empty(Gateway.Checkouts);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Refuses_expired_hold_and_unconnected_shop()
    {
        await using var db = Fixture.CreateContext();
        var connected = await SeedConnectedShopAsync(db);
        var unconnected = await TestData.SeedShopAsync(db);
        var expired = await HoldAsync(db, connected, TenAm);
        var noAccount = await HoldAsync(db, unconnected, TenAm);

        var notConnected = await Deposits(db).StartCheckoutAsync(noAccount.Id, "https://ok", "https://cancel");
        Clock.Now = Now.AddMinutes(31);
        var tooLate = await Deposits(db).StartCheckoutAsync(expired.Id, "https://ok", "https://cancel");

        Assert.Equal("shop_not_connected", Assert.IsType<StartCheckoutResult.Invalid>(notConnected).Reason);
        Assert.Equal("hold_expired", Assert.IsType<StartCheckoutResult.Invalid>(tooLate).Reason);
        Assert.Empty(Gateway.Checkouts);
    }
}
