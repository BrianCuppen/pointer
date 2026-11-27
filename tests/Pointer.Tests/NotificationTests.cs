using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class NotificationTests(PostgresFixture fixture) : DepositTestBase(fixture)
{
    private const string Owner = "jan@kapperjan.test";
    private Guid _shopId;

    private async Task<SeededShop> SeedShopWithOwnerAsync(PointerDbContext db, Action<Shop>? configure = null)
    {
        // The queue is global and the test database is shared: park what other tests left behind.
        await db.Notifications.Where(n => n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.Status, NotificationStatus.Skipped));
        var s = await SeedConnectedShopAsync(db);
        _shopId = s.Shop.Id;
        s.Shop.OwnerEmail = Owner;
        s.Shop.Address = "Kerkstraat 1, 9000 Gent";
        configure?.Invoke(s.Shop);
        await db.SaveChangesAsync();
        return s;
    }

    private async Task<List<Notification>> NotificationsFor(Guid shopId)
    {
        await using var db = Fixture.CreateContext();
        return await db.Notifications.AsNoTracking().Where(n => n.ShopId == shopId).OrderBy(n => n.CreatedUtc).ToListAsync();
    }

    private async Task<int> ProcessAsync(PaymentEvent evt)
    {
        await using var db = Fixture.CreateContext();
        await Webhooks(db).ProcessAsync(evt);
        return 0;
    }

    private async Task DispatchAsync()
    {
        await using var db = Fixture.CreateContext();
        // The reminder job is global; leave other tests' bookings out of this test's mailbox.
        await db.Notifications.Where(n => n.Status == NotificationStatus.Pending && n.ShopId != _shopId)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.Status, NotificationStatus.Skipped));
        await Dispatcher(db).DispatchDueAsync(100);
    }

    /// <summary>A confirmed booking inserted directly, created at the current fake time.</summary>
    private async Task<Booking> ConfirmedAsync(PointerDbContext db, SeededShop s, DateTime start)
    {
        var booking = new Booking
        {
            ShopId = s.Shop.Id, ServiceId = s.Haircut.Id, StaffId = s.Owner.Id,
            StartUtc = start, EndUtc = start.AddMinutes(30), Status = BookingStatus.Confirmed,
            CustomerName = "An Peeters", CustomerEmail = "an@example.com", DepositCents = 500,
            NoShowPolicy = s.Shop.NoShowPolicy, FreeCancellationMinutes = s.Shop.FreeCancellationMinutes,
            CreatedUtc = Clock.Now.UtcDateTime,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    [Fact]
    public async Task Webhook_delivered_twice_queues_one_confirmation_and_one_owner_email()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        await ProcessAsync(Paid(session, $"evt_a_{session}"));
        await ProcessAsync(Paid(session, $"evt_a_{session}"));   // same event again
        await ProcessAsync(Paid(session, $"evt_b_{session}"));   // async_payment_succeeded after completed

        var types = (await NotificationsFor(s.Shop.Id)).Select(n => n.Type).OrderBy(t => t).ToList();
        Assert.Equal([NotificationType.OwnerNewBooking, NotificationType.CustomerConfirmation], types);
    }

    [Fact]
    public async Task Owner_without_instant_emails_gets_no_new_booking_email()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerInstantEmails = false);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        await ProcessAsync(Paid(session, $"evt_{session}"));

        Assert.Equal([NotificationType.CustomerConfirmation], (await NotificationsFor(s.Shop.Id)).Select(n => n.Type));
    }

    [Fact]
    public async Task Confirmation_is_sent_in_shop_time_with_calendar_file_and_no_body_stored()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        var (booking, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await ProcessAsync(Paid(session, $"evt_{session}"));

        await DispatchAsync();

        var confirmation = Assert.Single(Email.Sent, m => m.To == "an@example.com");
        Assert.Contains("woensdag 2 december 2026 om 11:00", confirmation.TextBody); // 10:00 UTC = 11:00 Brussels
        Assert.Contains("Kerkstraat 1, 9000 Gent", confirmation.TextBody);
        Assert.Contains("houdt Kapper Jan het voorschot", confirmation.TextBody);
        Assert.Contains($"https://pointer.test/annuleren/{booking.CancelToken}", confirmation.TextBody);
        var ics = Assert.Single(confirmation.Attachments);
        Assert.Contains("DTSTART:20261202T100000Z", System.Text.Encoding.UTF8.GetString(ics.Content));

        var owner = Assert.Single(Email.Sent, m => m.To == Owner);
        Assert.Contains("An Peeters, an@example.com", owner.TextBody);

        var rows = await NotificationsFor(s.Shop.Id);
        Assert.All(rows, n => Assert.Equal(NotificationStatus.Sent, n.Status));
        Assert.Contains(rows, n => n.Recipient == "an@example.com");
    }

    [Fact]
    public async Task Out_of_order_events_end_with_only_the_confirmation_sent()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerInstantEmails = false);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);

        await ProcessAsync(Expired(session, $"evt_exp_{session}"));   // arrives first
        await ProcessAsync(Paid(session, $"evt_paid_{session}"));     // slot still free: reclaimed
        await DispatchAsync();

        var sent = Assert.Single(Email.Sent);
        Assert.StartsWith("Bevestiging", sent.Subject);
        var failed = Assert.Single(await NotificationsFor(s.Shop.Id), n => n.Type == NotificationType.CustomerPaymentFailed);
        Assert.Equal(NotificationStatus.Skipped, failed.Status);
    }

    [Fact]
    public async Task Paid_after_slot_was_taken_tells_the_customer_about_the_refund()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerInstantEmails = false);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await ProcessAsync(Expired(session, $"evt_exp_{session}"));
        await using (var other = Fixture.CreateContext())
            await HoldAsync(other, s, TenAm, "someone@example.com");

        await ProcessAsync(Paid(session, $"evt_paid_{session}"));
        await DispatchAsync();

        var sent = Assert.Single(Email.Sent);
        Assert.Contains("wordt terugbetaald", sent.TextBody);
        Assert.Contains("https://pointer.test/", sent.TextBody);
    }

    [Fact]
    public async Task Failed_send_is_retried_later()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerInstantEmails = false);
        var (_, session) = await HoldAndCheckoutAsync(db, s, TenAm);
        await ProcessAsync(Paid(session, $"evt_{session}"));

        Email.FailNext = 1;
        await DispatchAsync();
        var row = Assert.Single(await NotificationsFor(s.Shop.Id));
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Equal("HttpRequestException", row.LastError);

        await DispatchAsync();                       // backoff not over yet
        Assert.Empty(Email.Sent);
        Clock.Now = Clock.Now.AddMinutes(3);
        await DispatchAsync();
        Assert.Single(Email.Sent);
    }

    [Fact]
    public async Task Reminder_is_queued_once_at_the_right_time_and_skips_cancelled_bookings()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        var booking = await ConfirmedAsync(db, s, TenAm);                       // reminder due 1 Dec 10:00 UTC
        var cancelled = await ConfirmedAsync(db, s, TenAm.AddHours(2));
        cancelled.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        Clock.Now = new DateTimeOffset(2026, 12, 1, 9, 59, 0, TimeSpan.Zero);
        await Scheduler(db).EnqueueDueRemindersAsync();
        Assert.Empty(await NotificationsFor(s.Shop.Id));

        Clock.Now = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        await Scheduler(db).EnqueueDueRemindersAsync();
        await Scheduler(db).EnqueueDueRemindersAsync();                         // e.g. after a restart

        var reminder = Assert.Single(await NotificationsFor(s.Shop.Id));
        Assert.Equal((NotificationType.CustomerReminder, booking.Id), (reminder.Type, reminder.BookingId!.Value));
    }

    [Fact]
    public async Task Reminder_cancelled_before_sending_is_skipped()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        var booking = await ConfirmedAsync(db, s, TenAm);
        Clock.Now = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        await Scheduler(db).EnqueueDueRemindersAsync();
        booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        await DispatchAsync();

        Assert.Empty(Email.Sent);
        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await NotificationsFor(s.Shop.Id)).Status);
    }

    [Fact]
    public async Task Short_notice_booking_gets_no_reminder()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        Clock.Now = new DateTimeOffset(2026, 12, 2, 6, 0, 0, TimeSpan.Zero);   // booked 4 hours ahead
        await ConfirmedAsync(db, s, TenAm);

        await Scheduler(db).EnqueueDueRemindersAsync();

        Assert.Empty(await NotificationsFor(s.Shop.Id));
    }

    [Fact]
    public async Task Reminder_across_the_spring_dst_change_shows_summer_time()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db);
        Clock.Now = new DateTimeOffset(2027, 3, 20, 8, 0, 0, TimeSpan.Zero);
        var monday = new DateTime(2027, 3, 29, 7, 0, 0, DateTimeKind.Utc);       // 09:00 CEST, the day after the change
        await ConfirmedAsync(db, s, monday);

        Clock.Now = new DateTimeOffset(2027, 3, 28, 6, 59, 0, TimeSpan.Zero);
        await Scheduler(db).EnqueueDueRemindersAsync();
        Assert.Empty(await NotificationsFor(s.Shop.Id));

        Clock.Now = new DateTimeOffset(2027, 3, 28, 7, 0, 0, TimeSpan.Zero);    // exactly 24 hours before
        await Scheduler(db).EnqueueDueRemindersAsync();
        await DispatchAsync();

        Assert.Contains("maandag 29 maart 2027 om 09:00", Assert.Single(Email.Sent).TextBody);
    }

    [Theory]
    [InlineData(12 * 60, true)]  // free cancellation until 12h before: still open 24h before
    [InlineData(48 * 60, false)] // free cancellation ended 48h before: no link in a 24h reminder
    public async Task Reminder_shows_cancel_link_only_inside_the_free_window(int freeMinutes, bool expectLink)
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.FreeCancellationMinutes = freeMinutes);
        var booking = await ConfirmedAsync(db, s, TenAm);
        Clock.Now = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        await Scheduler(db).EnqueueDueRemindersAsync();

        await DispatchAsync();

        var body = Assert.Single(Email.Sent).TextBody;
        Assert.Equal(expectLink, body.Contains($"/annuleren/{booking.CancelToken}"));
    }

    [Fact]
    public async Task Daily_overview_goes_out_once_from_seven_local_and_lists_the_day()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerDailyOverview = true);
        await ConfirmedAsync(db, s, TenAm);                                    // 11:00 Brussels on 2 Dec
        await ConfirmedAsync(db, s, TenAm.AddDays(1));                         // other day

        Clock.Now = new DateTimeOffset(2026, 12, 2, 5, 59, 0, TimeSpan.Zero);  // 06:59 local
        await Scheduler(db).EnqueueDailyOverviewsAsync();
        Assert.Empty(await NotificationsFor(s.Shop.Id));

        Clock.Now = new DateTimeOffset(2026, 12, 2, 6, 5, 0, TimeSpan.Zero);   // 07:05 local
        await Scheduler(db).EnqueueDailyOverviewsAsync();
        await Scheduler(db).EnqueueDailyOverviewsAsync();
        await DispatchAsync();

        var overview = Assert.Single(Email.Sent);
        Assert.Equal(Owner, overview.To);
        Assert.Contains("11:00  Knippen  An Peeters", overview.TextBody);
        Assert.Single(overview.TextBody.Split('\n'), l => l.Contains("Knippen"));
    }

    [Fact]
    public async Task Daily_overview_is_skipped_on_an_empty_day()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerDailyOverview = true);
        Clock.Now = new DateTimeOffset(2026, 12, 2, 6, 5, 0, TimeSpan.Zero);

        await Scheduler(db).EnqueueDailyOverviewsAsync();
        await DispatchAsync();

        Assert.Empty(Email.Sent);
        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await NotificationsFor(s.Shop.Id)).Status);
    }

    [Fact]
    public async Task Booking_without_deposit_queues_a_confirmation()
    {
        await using var db = Fixture.CreateContext();
        var s = await SeedShopWithOwnerAsync(db, shop => shop.OwnerInstantEmails = false);
        s.Haircut.DepositCents = 0;
        await db.SaveChangesAsync();
        var booking = await HoldAsync(db, s, TenAm);

        await Deposits(db).StartCheckoutAsync(booking.Id, "https://ok", "https://cancel");

        Assert.Equal([NotificationType.CustomerConfirmation], (await NotificationsFor(s.Shop.Id)).Select(n => n.Type));
    }
}
