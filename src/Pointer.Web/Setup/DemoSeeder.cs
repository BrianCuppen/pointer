using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Data;
using Pointer.Web.Auth;

namespace Pointer.Web.Setup;

/// <summary>
/// Creates a demo barbershop so the app can be tried right after cloning.
/// Runs only when Demo__Seed=true, and only once (skipped if the demo slug exists).
/// </summary>
public static class DemoSeeder
{
    public const string Slug = "kapper-demo";

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
        db.Services.AddRange(
            new Pointer.Core.Domain.Service { ShopId = shop.Id, Name = "Knippen", DurationMinutes = 30, PriceCents = 2500, DepositCents = 500 },
            new Pointer.Core.Domain.Service { ShopId = shop.Id, Name = "Knippen en baard", DurationMinutes = 45, PriceCents = 3500, DepositCents = 1000 },
            new Pointer.Core.Domain.Service { ShopId = shop.Id, Name = "Baard trimmen", DurationMinutes = 15, PriceCents = 1200, DepositCents = 0 });
        await db.SaveChangesAsync();
        logger.LogInformation("Demo shop created: /{Slug}, admin login {Email}", Slug, email);
    }
}
