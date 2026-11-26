using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Tests;

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed record SeededShop(Shop Shop, Service Haircut, Staff Owner);

public static class TestData
{
    /// <summary>A shop with one 30 minute service and its single default staff member.</summary>
    public static async Task<SeededShop> SeedShopAsync(PointerDbContext db, string? slug = null)
    {
        var shop = new Shop
        {
            Slug = slug ?? $"shop-{Guid.NewGuid():N}"[..20],
            Name = "Kapper Jan",
            CreatedUtc = DateTime.UtcNow,
        };
        var haircut = new Service { ShopId = shop.Id, Name = "Knippen", DurationMinutes = 30, PriceCents = 2500, DepositCents = 500 };
        var owner = new Staff { ShopId = shop.Id, Name = "Jan" };
        db.Shops.Add(shop);
        db.Services.Add(haircut);
        db.Staff.Add(owner);
        await db.SaveChangesAsync();
        return new SeededShop(shop, haircut, owner);
    }
}
