using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pointer.Core.Bookings;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DoubleBookingTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime TenAm = new(2026, 12, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Now);

    private BookingService CreateService(PointerDbContext db) =>
        new(db, _clock, Options.Create(new BookingOptions { PaymentHoldMinutes = 15 }));

    private static CreateBookingRequest Request(SeededShop s, DateTime start, Guid? staffId = null) =>
        new(s.Shop.Id, s.Haircut.Id, staffId ?? s.Owner.Id, start, "An Peeters", "an@example.com", null);

    [Fact]
    public async Task Creates_pending_booking_with_policy_snapshot()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);

        var result = await CreateService(db).CreatePendingAsync(Request(s, TenAm));

        var created = Assert.IsType<CreateBookingResult.Created>(result).Booking;
        Assert.Equal(BookingStatus.PendingPayment, created.Status);
        Assert.Equal(TenAm.AddMinutes(30), created.EndUtc);
        Assert.Equal(Now.UtcDateTime.AddMinutes(15), created.HoldExpiresUtc);
        Assert.Equal(500, created.DepositCents);
        Assert.Equal(NoShowPolicy.KeepDeposit, created.NoShowPolicy);
        Assert.Equal(24 * 60, created.FreeCancellationMinutes);
    }

    [Theory]
    [InlineData(0)]    // exact same slot
    [InlineData(15)]   // starts in the middle
    [InlineData(-15)]  // ends in the middle
    [InlineData(29)]   // one minute overlap
    public async Task Rejects_overlapping_booking(int offsetMinutes)
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);
        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm)));

        var second = await service.CreatePendingAsync(Request(s, TenAm.AddMinutes(offsetMinutes)));

        Assert.IsType<CreateBookingResult.SlotTaken>(second);
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.ShopId == s.Shop.Id));
    }

    [Fact]
    public async Task Allows_back_to_back_bookings()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);

        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm)));
        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm.AddMinutes(30))));
        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm.AddMinutes(-30))));
    }

    [Fact]
    public async Task Allows_same_time_for_different_staff()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var second = new Staff { ShopId = s.Shop.Id, Name = "Els" };
        db.Staff.Add(second);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm)));
        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm, second.Id)));
    }

    [Fact]
    public async Task Cancelled_booking_frees_the_slot()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);
        var first = Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm))).Booking;

        first.Status = BookingStatus.Cancelled;
        first.HoldExpiresUtc = null;
        await db.SaveChangesAsync();

        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm)));
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public async Task Non_cancelled_statuses_keep_blocking(BookingStatus status)
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);
        var first = Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm))).Booking;
        first.Status = status;
        first.HoldExpiresUtc = null;
        await db.SaveChangesAsync();

        Assert.IsType<CreateBookingResult.SlotTaken>(await service.CreatePendingAsync(Request(s, TenAm)));
    }

    [Fact]
    public async Task Unexpired_payment_hold_blocks_the_slot()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);
        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm)));

        _clock.Now = Now.AddMinutes(14);

        Assert.IsType<CreateBookingResult.SlotTaken>(await service.CreatePendingAsync(Request(s, TenAm)));
    }

    [Fact]
    public async Task Expired_payment_hold_is_released_for_a_new_booking()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);
        var abandoned = Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm))).Booking;

        _clock.Now = Now.AddMinutes(16);

        Assert.IsType<CreateBookingResult.Created>(await service.CreatePendingAsync(Request(s, TenAm.AddMinutes(10))));
        var reloaded = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == abandoned.Id);
        Assert.Equal(BookingStatus.Cancelled, reloaded.Status);
        Assert.NotNull(reloaded.CancelledUtc);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_slot_produce_exactly_one_booking()
    {
        SeededShop s;
        await using (var setup = fixture.CreateContext())
            s = await TestData.SeedShopAsync(setup);

        const int attempts = 20;
        using var gate = new SemaphoreSlim(0);
        var tasks = Enumerable.Range(0, attempts).Select(async i =>
        {
            await using var db = fixture.CreateContext();
            var service = CreateService(db);
            await gate.WaitAsync();
            // Different start minutes, all overlapping 10:00-10:30.
            return await service.CreatePendingAsync(Request(s, TenAm.AddMinutes(i % 10)));
        }).ToList();
        gate.Release(attempts);
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r is CreateBookingResult.Created));
        Assert.Equal(attempts - 1, results.Count(r => r is CreateBookingResult.SlotTaken));
        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.Bookings.CountAsync(b => b.ShopId == s.Shop.Id));
    }

    [Fact]
    public async Task Database_rejects_overlap_even_when_application_code_is_bypassed()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        db.Bookings.Add(NewBooking(s, TenAm, BookingStatus.Confirmed));
        db.Bookings.Add(NewBooking(s, TenAm.AddMinutes(10), BookingStatus.Confirmed));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, pg.SqlState);
        Assert.Equal(PointerDbContext.BookingOverlapConstraint, pg.ConstraintName);
    }

    [Fact]
    public async Task Database_rejects_booking_another_shops_service()
    {
        await using var db = fixture.CreateContext();
        var a = await TestData.SeedShopAsync(db);
        var b = await TestData.SeedShopAsync(db);
        var crossTenant = NewBooking(a, TenAm, BookingStatus.Confirmed);
        crossTenant.ServiceId = b.Haircut.Id;
        db.Bookings.Add(crossTenant);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Rejects_start_in_the_past_and_non_utc_times()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var service = CreateService(db);

        var past = await service.CreatePendingAsync(Request(s, Now.UtcDateTime.AddMinutes(-1)));
        var local = await service.CreatePendingAsync(Request(s, DateTime.SpecifyKind(TenAm, DateTimeKind.Unspecified)));

        Assert.Equal("start_in_past", Assert.IsType<CreateBookingResult.Invalid>(past).Reason);
        Assert.Equal("start_must_be_utc", Assert.IsType<CreateBookingResult.Invalid>(local).Reason);
    }

    private static Booking NewBooking(SeededShop s, DateTime start, BookingStatus status) => new()
    {
        ShopId = s.Shop.Id,
        ServiceId = s.Haircut.Id,
        StaffId = s.Owner.Id,
        StartUtc = start,
        EndUtc = start.AddMinutes(30),
        Status = status,
        CustomerName = "Test",
        CustomerEmail = "test@example.com",
        CreatedUtc = DateTime.UtcNow,
    };
}
