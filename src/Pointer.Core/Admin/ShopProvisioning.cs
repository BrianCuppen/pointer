using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Admin;

/// <summary>
/// Creates a shop with its default staff member, sensible opening hours and an owner login.
/// Shops are set up by hand for pilots (no self sign-up), through the CLI or the demo seeder.
/// </summary>
public sealed class ShopProvisioning(PointerDbContext db, TimeProvider clock)
{
    public async Task<Shop> CreateAsync(string slug, string name, string ownerEmail, string passwordHash, CancellationToken ct = default)
    {
        slug = slug.Trim().ToLowerInvariant();
        if (await db.Shops.AnyAsync(s => s.Slug == slug, ct))
            throw new InvalidOperationException($"A shop with slug '{slug}' already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        var shop = new Shop { Slug = slug, Name = name.Trim(), OwnerEmail = ownerEmail.Trim().ToLowerInvariant(), CreatedUtc = now };
        db.Shops.Add(shop);
        db.Staff.Add(new Staff { ShopId = shop.Id, Name = name.Trim() });

        // Tuesday to Saturday 09:00-18:00, a common barbershop week. The owner adjusts it in the admin.
        foreach (var day in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
            db.OpeningHours.Add(new OpeningHours { ShopId = shop.Id, DayOfWeek = day, Opens = new(9, 0), Closes = new(18, 0) });

        db.ShopUsers.Add(new ShopUser { ShopId = shop.Id, Email = shop.OwnerEmail, PasswordHash = passwordHash, CreatedUtc = now });
        await db.SaveChangesAsync(ct);
        return shop;
    }
}
