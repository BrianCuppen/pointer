using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Bookings;

/// <summary>Read side of the public booking page. Only active, bookable data of one shop.</summary>
public sealed class PublicBookingQueries(PointerDbContext db)
{
    public Task<Shop?> FindShopAsync(string slug, CancellationToken ct = default) =>
        db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug.ToLower(), ct);

    public Task<Shop?> FindShopByIdAsync(Guid shopId, CancellationToken ct = default) =>
        db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId, ct);

    public Task<List<Service>> ActiveServicesAsync(Guid shopId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().Where(s => s.ShopId == shopId && s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);

    public Task<Service?> ActiveServiceAsync(Guid shopId, Guid serviceId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == shopId && s.Id == serviceId && s.IsActive, ct);

    /// <summary>v1 has one implicit staff member per shop.</summary>
    public Task<Staff?> DefaultStaffAsync(Guid shopId, CancellationToken ct = default) =>
        db.Staff.AsNoTracking().Where(s => s.ShopId == shopId && s.IsActive).OrderBy(s => s.Name).FirstOrDefaultAsync(ct);
}
