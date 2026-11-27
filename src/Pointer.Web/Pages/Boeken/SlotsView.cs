using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Boeken;

public sealed record SlotsView(string Slug, Guid ServiceId, DateOnly Date, IReadOnlyList<AvailableSlot> Slots)
{
    public static async Task<SlotsView> LoadAsync(PublicBookingQueries queries, AvailabilityService availability,
        Shop shop, Service service, DateOnly date, TimeZoneInfo zone, CancellationToken ct)
    {
        var staff = await queries.DefaultStaffAsync(shop.Id, ct);
        var slots = staff is null
            ? []
            : await availability.GetSlotsAsync(shop, service, staff.Id, date, date, ct);
        return new SlotsView(shop.Slug, service.Id, date, slots);
    }
}
