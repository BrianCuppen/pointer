using Microsoft.EntityFrameworkCore;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Admin;

public sealed record CalendarEntry(
    Guid BookingId,
    DateTimeOffset StartLocal,
    DateTimeOffset EndLocal,
    string Service,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    BookingStatus Status,
    long DepositCents,
    PaymentStatus? PaymentStatus,
    bool HasStarted);

public sealed record CalendarDay(DateOnly Date, IReadOnlyList<CalendarEntry> Entries);

/// <summary>The owner's week view. Live holds are shown (the slot is taken); cancelled bookings are not.</summary>
public sealed class CalendarQueries(PointerDbContext db, TimeProvider clock)
{
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public async Task<IReadOnlyList<CalendarDay>> WeekAsync(Shop shop, DateOnly anyDayLocal, CancellationToken ct = default)
    {
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        var monday = MondayOf(anyDayLocal);
        var from = ShopTime.ToUtc(monday, TimeOnly.MinValue, zone);
        var to = ShopTime.ToUtc(monday.AddDays(7), TimeOnly.MinValue, zone);
        var now = clock.GetUtcNow().UtcDateTime;

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Payments)
            .Where(b => b.ShopId == shop.Id && b.StartUtc >= from && b.StartUtc < to
                        && b.Status != BookingStatus.Cancelled
                        && !(b.Status == BookingStatus.PendingPayment && b.HoldExpiresUtc < now))
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);

        var entries = bookings.Select(b => new CalendarEntry(
            b.Id,
            ShopTime.ToLocal(b.StartUtc, zone),
            ShopTime.ToLocal(b.EndUtc, zone),
            b.Service!.Name,
            b.CustomerName,
            b.CustomerEmail,
            b.CustomerPhone,
            b.Status,
            b.DepositCents,
            b.Payments.OrderByDescending(p => p.CreatedUtc).FirstOrDefault()?.Status,
            b.StartUtc <= now)).ToList();

        return Enumerable.Range(0, 7)
            .Select(i => monday.AddDays(i))
            .Select(d => new CalendarDay(d, entries.Where(e => DateOnly.FromDateTime(e.StartLocal.DateTime) == d).ToList()))
            .ToList();
    }
}
