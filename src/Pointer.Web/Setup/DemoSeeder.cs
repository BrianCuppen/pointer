using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Web.Auth;

namespace Pointer.Web.Setup;

/// <summary>
/// Creates a demo barbershop so the app can be tried right after cloning: services, a day off and a
/// week of sample bookings around today, so the calendar is not empty.
/// Runs only when Demo__Seed=true, and only once (skipped if the demo slug exists).
/// All customers are made up and use example.com addresses.
/// </summary>
public static class DemoSeeder
{
    public const string Slug = "kapper-demo";

    private static readonly string[] Customers =
        ["Lotte Peeters", "Jonas Maes", "Emma Claes", "Lucas Wouters", "Noor Jacobs", "Arthur Willems", "Lina Goossens", "Finn Mertens"];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var db = services.GetRequiredService<PointerDbContext>();
        if (await db.Shops.AnyAsync(s => s.Slug == Slug))
            return;

        var email = config["Demo:OwnerEmail"] ?? "demo@pointer.test";
        var password = config["Demo:OwnerPassword"];
        if (string.IsNullOrEmpty(password))
        {
            logger.LogWarning("Demo__Seed is on but Demo__OwnerPassword is empty; skipping demo data");
            return;
        }

        var auth = services.GetRequiredService<OwnerAuth>();
        var shop = await services.GetRequiredService<ShopProvisioning>()
            .CreateAsync(Slug, "Kapper Demo", email, auth.HashPassword(password));

        var tracked = await db.Shops.SingleAsync(s => s.Id == shop.Id);
        tracked.Address = "Veldstraat 1, 9000 Gent";
        // Stripe test-mode account id, if provided, so the demo can take test payments.
        tracked.PaymentAccountId = config["Demo:StripeAccountId"];
        Service[] menu =
        [
            new() { ShopId = shop.Id, Name = "Knippen", DurationMinutes = 30, PriceCents = 2500, DepositCents = 500 },
            new() { ShopId = shop.Id, Name = "Knippen en baard", DurationMinutes = 45, PriceCents = 3500, DepositCents = 1000 },
            new() { ShopId = shop.Id, Name = "Baard trimmen", DurationMinutes = 15, PriceCents = 1200, DepositCents = 0 },
        ];
        db.Services.AddRange(menu);

        var staff = await db.Staff.SingleAsync(s => s.ShopId == shop.Id);
        var openDays = await db.OpeningHours.Where(h => h.ShopId == shop.Id).Select(h => h.DayOfWeek).ToListAsync();
        var zone = ShopTime.FindZone(tracked.TimeZoneId);
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var today = ShopTime.LocalDate(now, zone);

        // A day off two weeks from now, to show how days off block the booking page.
        var dayOff = today.AddDays(14);
        db.TimeOff.Add(new TimeOff
        {
            ShopId = shop.Id,
            StartUtc = ShopTime.ToUtc(dayOff, TimeOnly.MinValue, zone),
            EndUtc = ShopTime.ToUtc(dayOff.AddDays(1), TimeOnly.MinValue, zone),
            Note = "Opleiding",
        });

        TimeOnly[] times = [new(10, 0), new(11, 30), new(14, 0), new(16, 15)];
        var n = 0;
        for (var day = today.AddDays(-4); day <= today.AddDays(5); day = day.AddDays(1))
        {
            if (!openDays.Contains(day.DayOfWeek))
                continue;
            foreach (var time in times.Where((_, i) => (i + day.DayNumber) % 3 != 0))
            {
                var service = menu[n % menu.Length];
                var start = ShopTime.ToUtc(day, time, zone);
                var end = start.AddMinutes(service.DurationMinutes);
                var name = Customers[n % Customers.Length];
                var status = end > now ? BookingStatus.Confirmed
                    : n % 7 == 3 ? BookingStatus.NoShow
                    : BookingStatus.Completed;
                var booking = new Booking
                {
                    ShopId = shop.Id, ServiceId = service.Id, StaffId = staff.Id, StartUtc = start, EndUtc = end, Status = status,
                    CustomerName = name,
                    CustomerEmail = name.ToLowerInvariant().Replace(' ', '.') + "@example.com",
                    DepositCents = service.DepositCents, NoShowPolicy = tracked.NoShowPolicy,
                    FreeCancellationMinutes = tracked.FreeCancellationMinutes, CreatedUtc = start.AddDays(-3),
                };
                if (service.DepositCents > 0)
                    booking.Payments.Add(new Payment
                    {
                        Provider = "stripe", ProviderPaymentId = $"cs_demo_{n}", AmountCents = service.DepositCents,
                        Status = PaymentStatus.Succeeded, CreatedUtc = booking.CreatedUtc, UpdatedUtc = booking.CreatedUtc,
                    });
                db.Bookings.Add(booking);
                n++;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Demo shop created: /{Slug} with {Count} sample bookings, admin login {Email}", Slug, n, email);
    }
}
