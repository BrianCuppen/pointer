using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Notifications;

/// <summary>Queues time-based messages. Safe to run often and on several instances: the unique indexes dedupe.</summary>
public sealed class NotificationScheduler(PointerDbContext db, TimeProvider clock, IOptions<NotificationOptions> options)
{
    /// <summary>
    /// Queues a reminder for each confirmed booking whose reminder time has come and that has not started.
    /// Bookings made after their reminder time (booked at short notice) get no reminder.
    /// </summary>
    public Task<int> EnqueueDueRemindersAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications (id, shop_id, booking_id, type, status, attempts, created_utc)
            SELECT gen_random_uuid(), b.shop_id, b.id, 'customer_reminder', 'pending', 0, {now}
            FROM bookings b
            JOIN shops s ON s.id = b.shop_id
            WHERE b.status = 'confirmed'
              AND b.start_utc > {now}
              AND b.start_utc - make_interval(hours => s.reminder_hours_before) <= {now}
              AND b.created_utc < b.start_utc - make_interval(hours => s.reminder_hours_before)
            ON CONFLICT DO NOTHING
            """, ct);
    }

    /// <summary>
    /// Queues today's overview for shops that want one, from the overview time until noon local time
    /// (a late overview after a long outage is not useful). Days without appointments are skipped at send time.
    /// </summary>
    public async Task<int> EnqueueDailyOverviewsAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var shops = await db.Shops.AsNoTracking()
            .Where(s => s.OwnerDailyOverview && s.OwnerEmail != null)
            .Select(s => new { s.Id, s.TimeZoneId })
            .ToListAsync(ct);

        var queued = 0;
        foreach (var shop in shops)
        {
            var local = ShopTime.ToLocal(now, ShopTime.FindZone(shop.TimeZoneId));
            var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
            if (time < options.Value.DailyOverviewAt || time >= new TimeOnly(12, 0))
                continue;

            var day = DateOnly.FromDateTime(local.DateTime);
            queued += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notifications (id, shop_id, booking_id, type, overview_date, status, attempts, created_utc)
                VALUES ({Guid.NewGuid()}, {shop.Id}, NULL, {NotificationOutbox.Snake(NotificationType.OwnerDailyOverview)}, {day}, 'pending', 0, {now})
                ON CONFLICT DO NOTHING
                """, ct);
        }
        return queued;
    }
}
