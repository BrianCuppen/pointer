using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Tests;

public abstract class DepositTestBase(PostgresFixture fixture)
{
    protected const string Account = "acct_test_shop";
    protected static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    protected static readonly DateTime TenAm = new(2026, 12, 2, 10, 0, 0, DateTimeKind.Utc);

    protected readonly FakeClock Clock = new(Now);
    protected readonly FakePaymentGateway Gateway = new();
    protected PostgresFixture Fixture { get; } = fixture;

    protected BookingService Bookings(PointerDbContext db) =>
        new(db, new AvailabilityService(db, Clock, Options.Create(new AvailabilityOptions())), Clock,
            Options.Create(new BookingOptions { PaymentHoldMinutes = 30 }));

    protected DepositService Deposits(PointerDbContext db, long feeCents = 30) =>
        new(db, Gateway, Clock, Options.Create(new PlatformFeeOptions { PerDepositFeeCents = feeCents }));

    protected PaymentWebhookProcessor Webhooks(PointerDbContext db) =>
        new(db, Gateway, Clock, NullLogger<PaymentWebhookProcessor>.Instance);

    protected async Task<SeededShop> SeedConnectedShopAsync(PointerDbContext db)
    {
        var s = await TestData.SeedShopAsync(db);
        s.Shop.PaymentAccountId = Account;
        await db.SaveChangesAsync();
        return s;
    }

    protected async Task<Booking> HoldAsync(PointerDbContext db, SeededShop s, DateTime start, string email = "an@example.com")
    {
        var result = await Bookings(db).CreatePendingAsync(
            new CreateBookingRequest(s.Shop.Id, s.Haircut.Id, s.Owner.Id, start, "An Peeters", email, null));
        return Assert.IsType<CreateBookingResult.Created>(result).Booking;
    }

    /// <summary>A held booking with a started checkout. Returns the Checkout Session id.</summary>
    protected async Task<(Booking Booking, string SessionId)> HoldAndCheckoutAsync(PointerDbContext db, SeededShop s, DateTime start)
    {
        var booking = await HoldAsync(db, s, start);
        Assert.IsType<StartCheckoutResult.Redirect>(await Deposits(db).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel"));
        return (booking, $"cs_test_{booking.Id:N}");
    }

    protected static PaymentEvent Paid(string sessionId, string eventId = "evt_paid", string? account = Account) =>
        new("stripe", eventId, "checkout.session.completed", PaymentEventKind.CheckoutPaid, sessionId, $"pi_{sessionId}", account);

    protected static PaymentEvent Expired(string sessionId, string eventId = "evt_expired") =>
        new("stripe", eventId, "checkout.session.expired", PaymentEventKind.CheckoutFailed, sessionId, null, Account);

    protected static PaymentEvent Refunded(string sessionId, string eventId = "evt_refunded") =>
        new("stripe", eventId, "charge.refunded", PaymentEventKind.Refunded, null, $"pi_{sessionId}", Account);
}
