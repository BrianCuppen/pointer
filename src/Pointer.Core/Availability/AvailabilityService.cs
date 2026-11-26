using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Availability;

/// <summary>Loads schedule data for one staff member and hands it to <see cref="SlotCalculator"/>.</summary>
public sealed class AvailabilityService(PointerDbContext db, TimeProvider clock, IOptions<AvailabilityOptions> options)
{
    private readonly AvailabilityOptions _options = options.Value;

    /// <summary>Open start times for a service, by local calendar day (inclusive range).</summary>
    public async Task<IReadOnlyList<AvailableSlot>> GetSlotsAsync(
        Shop shop, Service service, Guid staffId, DateOnly fromLocal, DateOnly toLocal, CancellationToken ct = default)
    {
        var query = await BuildQueryAsync(shop, service, staffId, fromLocal, toLocal, includeBookings: true, ct);
        return SlotCalculator.Compute(query);
    }

    /// <summary>
    /// True if the slot fits opening hours, time off, notice and horizon. Existing bookings are
    /// deliberately not checked here: the database constraint decides that, race-free.
    /// </summary>
    public async Task<bool> IsWithinScheduleAsync(Shop shop, Service service, Guid staffId, DateTime startUtc, CancellationToken ct = default)
    {
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        var day = ShopTime.LocalDate(startUtc, zone);
        var query = await BuildQueryAsync(shop, service, staffId, day, day, includeBookings: false, ct);
        return SlotCalculator.IsBookable(query, startUtc);
    }

    private async Task<SlotQuery> BuildQueryAsync(
        Shop shop, Service service, Guid staffId, DateOnly fromLocal, DateOnly toLocal, bool includeBookings, CancellationToken ct)
    {
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        var now = clock.GetUtcNow().UtcDateTime;

        // Load a day of margin on both sides; the calculator does the exact filtering.
        var rangeStart = ShopTime.ToUtc(fromLocal, TimeOnly.MinValue, zone).AddDays(-1);
        var rangeEnd = ShopTime.ToUtc(toLocal.AddDays(1), TimeOnly.MinValue, zone).AddDays(1);

        var rows = await db.OpeningHours.AsNoTracking()
            .Where(h => h.ShopId == shop.Id && (h.StaffId == null || h.StaffId == staffId))
            .ToListAsync(ct);

        var busy = await db.TimeOff.AsNoTracking()
            .Where(t => t.ShopId == shop.Id && (t.StaffId == null || t.StaffId == staffId)
                        && t.StartUtc < rangeEnd && t.EndUtc > rangeStart)
            .Select(t => new BusyPeriod(t.StartUtc, t.EndUtc))
            .ToListAsync(ct);

        if (includeBookings)
        {
            busy.AddRange(await db.Bookings.AsNoTracking()
                .Where(b => b.StaffId == staffId
                            && b.Status != BookingStatus.Cancelled
                            && !(b.Status == BookingStatus.PendingPayment && b.HoldExpiresUtc < now)
                            && b.StartUtc < rangeEnd && b.EndUtc > rangeStart)
                .Select(b => new BusyPeriod(b.StartUtc, b.EndUtc))
                .ToListAsync(ct));
        }

        return new SlotQuery(zone, ResolveOpeningHours(rows, staffId), busy, service.DurationMinutes,
            fromLocal, toLocal, now, _options);
    }

    /// <summary>Per weekday, the staff member's own hours replace the shop's hours if any exist.</summary>
    internal static IReadOnlyList<OpenInterval> ResolveOpeningHours(IEnumerable<OpeningHours> rows, Guid staffId)
    {
        return rows
            .GroupBy(r => r.DayOfWeek)
            .SelectMany(day =>
            {
                var own = day.Where(r => r.StaffId == staffId).ToList();
                return own.Count > 0 ? own : day.Where(r => r.StaffId == null);
            })
            .Select(r => new OpenInterval(r.DayOfWeek, r.Opens, r.Closes))
            .ToList();
    }
}
