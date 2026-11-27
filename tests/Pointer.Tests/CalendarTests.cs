using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Web.Auth;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class CalendarTests(PostgresFixture fixture) : IDisposable
{
    private readonly WebAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private static Booking NewBooking(SeededShop s, DateTime startUtc, BookingStatus status, string name, DateTime? holdExpires = null) => new()
    {
        ShopId = s.Shop.Id, ServiceId = s.Haircut.Id, StaffId = s.Owner.Id,
        StartUtc = startUtc, EndUtc = startUtc.AddMinutes(30), Status = status, HoldExpiresUtc = holdExpires,
        CustomerName = name, CustomerEmail = $"{name.ToLowerInvariant()}@example.com", CreatedUtc = DateTime.UtcNow,
    };

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Monday_of_any_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), CalendarQueries.MondayOf(new DateOnly(2026, 10, 25)));
        Assert.Equal(new DateOnly(2026, 10, 26), CalendarQueries.MondayOf(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public async Task Week_uses_local_days_across_the_autumn_clock_change()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        // Sunday 25 Oct 2026: clocks go back at 03:00 CEST. 23:30 local (CET) is 22:30 UTC, still Sunday.
        db.Bookings.Add(NewBooking(s, Utc(10, 25, 22, 30), BookingStatus.Confirmed, "Late"));
        // Monday 26 Oct 00:30 local is 23:30 UTC on Sunday, but belongs to the next week.
        db.Bookings.Add(NewBooking(s, Utc(10, 25, 23, 30), BookingStatus.Confirmed, "Next"));
        // Monday 19 Oct 00:15 local (CEST) is 22:15 UTC on Sunday 18 Oct.
        db.Bookings.Add(NewBooking(s, Utc(10, 18, 22, 15), BookingStatus.Confirmed, "Early"));
        await db.SaveChangesAsync();

        var week = await new CalendarQueries(db, new FakeClock(new DateTimeOffset(Utc(10, 20, 8)))).WeekAsync(s.Shop, new DateOnly(2026, 10, 21));

        Assert.Equal(7, week.Count);
        Assert.Equal(new DateOnly(2026, 10, 19), week[0].Date);
        Assert.Equal("Early", Assert.Single(week[0].Entries).CustomerName);
        var sunday = Assert.Single(week[6].Entries);
        Assert.Equal("Late", sunday.CustomerName);
        Assert.Equal(new TimeSpan(23, 30, 0), sunday.StartLocal.TimeOfDay);
        Assert.DoesNotContain(week.SelectMany(d => d.Entries), e => e.CustomerName == "Next");
    }

    [Fact]
    public async Task Week_hides_cancelled_bookings_and_expired_holds_but_shows_live_holds()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var now = Utc(12, 1, 8);
        db.Bookings.Add(NewBooking(s, Utc(12, 2, 9), BookingStatus.Confirmed, "Confirmed"));
        db.Bookings.Add(NewBooking(s, Utc(12, 2, 10), BookingStatus.Cancelled, "Cancelled"));
        db.Bookings.Add(NewBooking(s, Utc(12, 2, 11), BookingStatus.PendingPayment, "Expired", holdExpires: now.AddMinutes(-1)));
        db.Bookings.Add(NewBooking(s, Utc(12, 2, 12), BookingStatus.PendingPayment, "Live", holdExpires: now.AddMinutes(10)));
        await db.SaveChangesAsync();

        var week = await new CalendarQueries(db, new FakeClock(new DateTimeOffset(now))).WeekAsync(s.Shop, new DateOnly(2026, 12, 2));

        var names = week.SelectMany(d => d.Entries).Select(e => e.CustomerName).ToList();
        Assert.Equal(["Confirmed", "Live"], names);
    }

    [Fact]
    public async Task Owner_marks_a_past_booking_as_no_show_but_cannot_touch_another_shops_booking()
    {
        Shop mine, theirs;
        Booking myBooking, theirBooking;
        string email;
        await using (var db = fixture.CreateContext())
        {
            (mine, email) = await ProvisionAsync(db);
            (theirs, _) = await ProvisionAsync(db);
            myBooking = await PastBookingAsync(db, mine, "Mine");
            theirBooking = await PastBookingAsync(db, theirs, "Theirs");
        }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);

        var week = CalendarQueries.MondayOf(DateOnly.FromDateTime(myBooking.StartUtc)).ToString("yyyy-MM-dd");
        var html = await client.GetStringAsync($"/beheer?week={week}");
        Assert.Contains("Mine", html);
        Assert.DoesNotContain("Theirs", html);
        var token = TokenRegex().Match(html).Groups[1].Value;

        var foreign = await client.PostAsync($"/beheer?handler=NoShow&id={theirBooking.Id}&week={week}", Form(token));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var own = await client.PostAsync($"/beheer?handler=NoShow&id={myBooking.Id}&week={week}", Form(token));
        Assert.Equal(HttpStatusCode.Redirect, own.StatusCode);

        await using var check = fixture.CreateContext();
        Assert.Equal(BookingStatus.NoShow, (await check.Bookings.SingleAsync(b => b.Id == myBooking.Id)).Status);
        Assert.Equal(BookingStatus.Confirmed, (await check.Bookings.SingleAsync(b => b.Id == theirBooking.Id)).Status);
    }

    private static FormUrlEncodedContent Form(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private const string Password = "calendar-password-1";

    private static async Task<(Shop, string)> ProvisionAsync(PointerDbContext db)
    {
        var slug = $"c-{Guid.NewGuid():N}"[..20];
        var email = $"{slug}@example.com";
        var hash = new OwnerAuth(db, new PasswordHasher<ShopUser>()).HashPassword(Password);
        return (await new ShopProvisioning(db, TimeProvider.System).CreateAsync(slug, "Zaak " + slug, email, hash), email);
    }

    /// <summary>A confirmed booking that started an hour ago, so it can be marked as a no-show.</summary>
    private static async Task<Booking> PastBookingAsync(PointerDbContext db, Shop shop, string name)
    {
        var service = new Service { ShopId = shop.Id, Name = "Knippen", DurationMinutes = 30 };
        db.Services.Add(service);
        var staff = await db.Staff.FirstOrDefaultAsync(x => x.ShopId == shop.Id);
        if (staff is null)
        {
            staff = new Staff { ShopId = shop.Id, Name = "Eigenaar" };
            db.Staff.Add(staff);
        }
        var start = DateTime.UtcNow.AddHours(-1);
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, DateTimeKind.Utc);
        var booking = new Booking
        {
            ShopId = shop.Id, ServiceId = service.Id, StaffId = staff.Id, StartUtc = start, EndUtc = start.AddMinutes(30),
            Status = BookingStatus.Confirmed, CustomerName = name, CustomerEmail = "x@example.com", CreatedUtc = DateTime.UtcNow,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/beheer/inloggen");
        var token = TokenRegex().Match(html).Groups[1].Value;
        var response = await client.PostAsync("/beheer/inloggen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = Password, ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
