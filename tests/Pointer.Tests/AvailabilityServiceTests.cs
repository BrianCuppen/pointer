using Microsoft.Extensions.Options;
using Pointer.Core.Availability;
using Pointer.Core.Domain;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AvailabilityServiceTests(PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 12, 2); // Wednesday, Brussels UTC+1
    private static DateTime Local(int h, int m = 0) => new(2026, 12, 2, h - 1, m, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));

    private AvailabilityService Create(Pointer.Core.Data.PointerDbContext db) =>
        new(db, _clock, Options.Create(new AvailabilityOptions()));

    private static Booking Booking(SeededShop s, DateTime start, BookingStatus status, DateTime? holdExpires = null) => new()
    {
        ShopId = s.Shop.Id, ServiceId = s.Haircut.Id, StaffId = s.Owner.Id,
        StartUtc = start, EndUtc = start.AddMinutes(30), Status = status, HoldExpiresUtc = holdExpires,
        CustomerName = "Test", CustomerEmail = "test@example.com", CreatedUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task Excludes_bookings_and_time_off_but_not_cancelled_or_expired_holds()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        db.Bookings.AddRange(
            Booking(s, Local(10), BookingStatus.Confirmed),
            Booking(s, Local(11), BookingStatus.Cancelled),
            Booking(s, Local(12), BookingStatus.PendingPayment, holdExpires: _clock.Now.UtcDateTime.AddMinutes(-1)),
            Booking(s, Local(13), BookingStatus.PendingPayment, holdExpires: _clock.Now.UtcDateTime.AddMinutes(10)));
        db.TimeOff.Add(new TimeOff { ShopId = s.Shop.Id, StartUtc = Local(15), EndUtc = Local(18) });
        await db.SaveChangesAsync();

        var starts = (await Create(db).GetSlotsAsync(s.Shop, s.Haircut, s.Owner.Id, Day, Day)).Select(x => x.StartUtc).ToList();

        Assert.Contains(Local(9), starts);
        Assert.DoesNotContain(Local(10), starts);      // confirmed
        Assert.Contains(Local(11), starts);            // cancelled
        Assert.Contains(Local(12), starts);            // expired hold
        Assert.DoesNotContain(Local(13), starts);      // live hold
        Assert.DoesNotContain(Local(15), starts);      // time off
        Assert.Equal(Local(14, 30), starts.Max());     // last slot before time off
    }

    [Fact]
    public async Task Staff_hours_replace_shop_hours_on_that_day_only()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        db.OpeningHours.Add(new OpeningHours { ShopId = s.Shop.Id, StaffId = s.Owner.Id, DayOfWeek = DayOfWeek.Wednesday, Opens = new(12, 0), Closes = new(13, 0) });
        await db.SaveChangesAsync();
        var service = Create(db);

        var wednesday = await service.GetSlotsAsync(s.Shop, s.Haircut, s.Owner.Id, Day, Day);
        var thursday = await service.GetSlotsAsync(s.Shop, s.Haircut, s.Owner.Id, Day.AddDays(1), Day.AddDays(1));

        Assert.Equal(Local(12), wednesday.Min(x => x.StartUtc));
        Assert.Equal(Local(12, 30), wednesday.Max(x => x.StartUtc));
        Assert.Equal(new TimeSpan(9, 0, 0), thursday.Min(x => x.StartLocal.TimeOfDay));
    }

    [Fact]
    public async Task Closed_day_has_no_slots()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        db.OpeningHours.RemoveRange(db.OpeningHours.Where(h => h.ShopId == s.Shop.Id && h.DayOfWeek == DayOfWeek.Wednesday));
        await db.SaveChangesAsync();

        Assert.Empty(await Create(db).GetSlotsAsync(s.Shop, s.Haircut, s.Owner.Id, Day, Day));
    }
}
