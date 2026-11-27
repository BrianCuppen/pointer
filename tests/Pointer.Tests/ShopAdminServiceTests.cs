using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Domain;
using Pointer.Web.Auth;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ShopAdminServiceTests(PostgresFixture fixture)
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Saves_a_service_in_cents_and_validates_input()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var admin = new ShopAdminService(db, _clock);

        var ok = await admin.SaveServiceAsync(s.Shop.Id, null, new ServiceInput(" Kleuren ", 90, 65.50m, 15m, true));
        var bad = await admin.SaveServiceAsync(s.Shop.Id, null, new ServiceInput("", 7, 10m, 20m, true));

        Assert.True(ok.Succeeded);
        var saved = await db.Services.AsNoTracking().SingleAsync(x => x.ShopId == s.Shop.Id && x.Name == "Kleuren");
        Assert.Equal((90, 6550L, 1500L), (saved.DurationMinutes, saved.PriceCents, saved.DepositCents));
        Assert.Equal(["Name", "DurationMinutes", "DepositEuro"], bad.Errors.Select(e => e.Field));
    }

    [Fact]
    public async Task Cannot_edit_another_shops_service()
    {
        await using var db = fixture.CreateContext();
        var mine = await TestData.SeedShopAsync(db);
        var theirs = await TestData.SeedShopAsync(db);
        var admin = new ShopAdminService(db, _clock);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            admin.SaveServiceAsync(mine.Shop.Id, theirs.Haircut.Id, new ServiceInput("Hacked", 30, 1m, 0m, true)));
        Assert.Null(await admin.GetServiceAsync(mine.Shop.Id, theirs.Haircut.Id));
        Assert.Equal("Knippen", (await db.Services.AsNoTracking().SingleAsync(x => x.Id == theirs.Haircut.Id)).Name);
    }

    [Fact]
    public async Task Weekly_hours_are_replaced_and_validated()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var admin = new ShopAdminService(db, _clock);
        var week = ShopAdminService.WeekOrder.Select(d => d == DayOfWeek.Saturday
            ? new HoursInput(d, new(9, 0), new(12, 0), new(13, 0), new(17, 0))
            : new HoursInput(d, null, null, null, null)).ToList();

        Assert.True((await admin.SaveWeekAsync(s.Shop.Id, week)).Succeeded);
        var reloaded = await admin.GetWeekAsync(s.Shop.Id);
        Assert.Equal(new HoursInput(DayOfWeek.Saturday, new(9, 0), new(12, 0), new(13, 0), new(17, 0)), reloaded.Single(d => d.Day == DayOfWeek.Saturday));
        Assert.All(reloaded.Where(d => d.Day != DayOfWeek.Saturday), d => Assert.Null(d.Opens));

        var invalid = await admin.SaveWeekAsync(s.Shop.Id, [new HoursInput(DayOfWeek.Monday, new(18, 0), new(9, 0), null, null)]);
        Assert.Equal("Error_Hours", Assert.Single(invalid.Errors).MessageKey);
        Assert.Equal(2, await db.OpeningHours.CountAsync(h => h.ShopId == s.Shop.Id)); // unchanged
    }

    [Fact]
    public async Task Day_off_covers_whole_local_days_across_dst()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var admin = new ShopAdminService(db, _clock);

        await admin.AddTimeOffAsync(s.Shop.Id, new TimeOffInput(new(2027, 3, 28), new(2027, 3, 28), "Pasen"));

        var off = await db.TimeOff.AsNoTracking().SingleAsync(t => t.ShopId == s.Shop.Id);
        Assert.Equal(new DateTime(2027, 3, 27, 23, 0, 0, DateTimeKind.Utc), off.StartUtc); // midnight CET
        Assert.Equal(new DateTime(2027, 3, 28, 22, 0, 0, DateTimeKind.Utc), off.EndUtc);   // next midnight CEST: a 23-hour day
    }

    [Fact]
    public async Task Settings_are_validated_and_saved()
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        var admin = new ShopAdminService(db, _clock);

        var bad = await admin.SaveSettingsAsync(s.Shop.Id, new SettingsInput("", null, "nope", NoShowPolicy.KeepDeposit, -1, 1, true, false, true, "sk_live_x"));
        var ok = await admin.SaveSettingsAsync(s.Shop.Id, new SettingsInput("Kapper Jan", "Markt 1", "Jan@Example.com", NoShowPolicy.RefundDeposit, 12, 6, false, true, false, "acct_123"));

        Assert.Equal(["Name", "FreeCancellationHours", "ReminderHoursBefore", "OwnerEmail", "PaymentAccountId"], bad.Errors.Select(e => e.Field));
        Assert.True(ok.Succeeded);
        var shop = await db.Shops.AsNoTracking().SingleAsync(x => x.Id == s.Shop.Id);
        Assert.Equal(("jan@example.com", 720, 6, NoShowPolicy.RefundDeposit, "acct_123"),
            (shop.OwnerEmail, shop.FreeCancellationMinutes, shop.ReminderHoursBefore, shop.NoShowPolicy, shop.PaymentAccountId));
    }

    [Fact]
    public async Task Provisioned_owner_can_sign_in_only_with_the_right_password()
    {
        await using var db = fixture.CreateContext();
        var auth = new OwnerAuth(db, new PasswordHasher<ShopUser>());
        var slug = $"p-{Guid.NewGuid():N}"[..20];
        var email = $"{slug}@example.com";
        var shop = await new ShopProvisioning(db, _clock).CreateAsync(slug, "Nieuwe Zaak", email.ToUpperInvariant(), auth.HashPassword("correct horse battery"));

        Assert.Equal(shop.Id, (await auth.ValidateAsync(email, "correct horse battery", default))?.ShopId);
        Assert.Null(await auth.ValidateAsync(email, "wrong password", default));
        Assert.Null(await auth.ValidateAsync("nobody@example.com", "correct horse battery", default));
        Assert.Equal(1, await db.Staff.CountAsync(x => x.ShopId == shop.Id));
        Assert.Equal(5, await db.OpeningHours.CountAsync(x => x.ShopId == shop.Id));
        Assert.DoesNotContain("correct horse", (await db.ShopUsers.AsNoTracking().SingleAsync(u => u.ShopId == shop.Id)).PasswordHash);
    }
}
