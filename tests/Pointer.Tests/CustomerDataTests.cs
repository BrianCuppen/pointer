using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Domain;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CustomerDataTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);

    private CustomerDataService Service(Pointer.Core.Data.PointerDbContext db) => new(db, new FakeClock(new DateTimeOffset(Now)));

    private static Booking NewBooking(SeededShop s, DateTime startUtc, BookingStatus status, string email) => new()
    {
        ShopId = s.Shop.Id, ServiceId = s.Haircut.Id, StaffId = s.Owner.Id,
        StartUtc = startUtc, EndUtc = startUtc.AddMinutes(30), Status = status,
        CustomerName = "An Peeters", CustomerEmail = email, CustomerPhone = "0470 00 00 00", CreatedUtc = Now.AddDays(-30),
    };

    [Fact]
    public async Task Export_contains_only_this_shops_bookings_for_this_email_case_insensitively()
    {
        await using var db = fixture.CreateContext();
        var mine = await TestData.SeedShopAsync(db);
        var other = await TestData.SeedShopAsync(db);
        db.Bookings.Add(NewBooking(mine, Now.AddDays(-3), BookingStatus.Completed, "An@Example.com"));
        db.Bookings.Add(NewBooking(mine, Now.AddDays(-2), BookingStatus.Cancelled, "an@example.com"));
        db.Bookings.Add(NewBooking(mine, Now.AddDays(-1), BookingStatus.Completed, "someone@example.com"));
        db.Bookings.Add(NewBooking(other, Now.AddDays(-1), BookingStatus.Completed, "an@example.com"));
        await db.SaveChangesAsync();

        var json = await Service(db).ExportJsonAsync(mine.Shop.Id, " AN@example.com ");

        using var doc = JsonDocument.Parse(json!);
        var bookings = doc.RootElement.GetProperty("bookings");
        Assert.Equal(2, bookings.GetArrayLength());
        Assert.Equal("completed", bookings[0].GetProperty("status").GetString());
        Assert.Equal("0470 00 00 00", bookings[0].GetProperty("customerPhone").GetString());
        Assert.Null(await Service(db).ExportJsonAsync(mine.Shop.Id, "nobody@example.com"));
    }

    [Fact]
    public async Task Erase_anonymises_bookings_keeps_payments_and_stops_pending_emails()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var past = NewBooking(s, Now.AddDays(-3), BookingStatus.Completed, "erase@example.com");
        past.Payments.Add(new Payment { Provider = "stripe", ProviderPaymentId = $"cs_{Guid.NewGuid():N}", AmountCents = 500, Status = PaymentStatus.Succeeded, CreatedUtc = Now.AddDays(-4) });
        var otherCustomer = NewBooking(s, Now.AddDays(-2), BookingStatus.Completed, "keep@example.com");
        db.Bookings.AddRange(past, otherCustomer);
        db.Notifications.Add(new Notification { ShopId = s.Shop.Id, BookingId = past.Id, Type = NotificationType.CustomerConfirmation, Status = NotificationStatus.Sent, Recipient = "erase@example.com", CreatedUtc = Now });
        db.Notifications.Add(new Notification { ShopId = s.Shop.Id, BookingId = past.Id, Type = NotificationType.CustomerNoShow, Status = NotificationStatus.Pending, CreatedUtc = Now });
        await db.SaveChangesAsync();

        Assert.Equal(CustomerDeleteResult.Deleted, await Service(db).EraseAsync(s.Shop.Id, "Erase@example.com"));

        await using var check = fixture.CreateContext();
        var erased = await check.Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == past.Id);
        Assert.Equal(("", "", null), (erased.CustomerName, erased.CustomerEmail, erased.CustomerPhone));
        Assert.Equal(500, erased.Payments.Single().AmountCents);
        var mails = await check.Notifications.Where(n => n.BookingId == past.Id).OrderBy(n => n.Type).ToListAsync();
        Assert.All(mails, n => Assert.Null(n.Recipient));
        Assert.Equal(NotificationStatus.Skipped, mails.Single(n => n.Type == NotificationType.CustomerNoShow).Status);
        Assert.Equal("keep@example.com", (await check.Bookings.SingleAsync(b => b.Id == otherCustomer.Id)).CustomerEmail);
    }

    [Fact]
    public async Task Erase_is_refused_while_the_customer_has_an_upcoming_booking()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        db.Bookings.Add(NewBooking(s, Now.AddDays(2), BookingStatus.Confirmed, "soon@example.com"));
        await db.SaveChangesAsync();

        Assert.Equal(CustomerDeleteResult.HasUpcomingBookings, await Service(db).EraseAsync(s.Shop.Id, "soon@example.com"));
        Assert.Equal(CustomerDeleteResult.NotFound, await Service(db).EraseAsync(s.Shop.Id, "nobody@example.com"));
        Assert.Equal(CustomerDeleteResult.NotFound, await Service(db).EraseAsync(s.Shop.Id, "  "));
    }

    [Fact]
    public async Task Another_shop_cannot_find_or_erase_the_customer()
    {
        await using var db = fixture.CreateContext();
        var mine = await TestData.SeedShopAsync(db);
        var other = await TestData.SeedShopAsync(db);
        db.Bookings.Add(NewBooking(mine, Now.AddDays(-1), BookingStatus.Completed, "private@example.com"));
        await db.SaveChangesAsync();

        Assert.Empty(await Service(db).FindAsync(other.Shop.Id, "private@example.com"));
        Assert.Equal(CustomerDeleteResult.NotFound, await Service(db).EraseAsync(other.Shop.Id, "private@example.com"));
    }
}
