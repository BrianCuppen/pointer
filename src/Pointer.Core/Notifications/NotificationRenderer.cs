using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Notifications;

/// <summary>
/// Builds an email from current data at send time. Returns null when the message is no longer
/// relevant (a reminder for a booking cancelled since, a "payment failed" for a booking that got
/// confirmed after all), so out-of-order changes end with the right set of emails.
/// </summary>
public sealed class NotificationRenderer(PointerDbContext db, TimeProvider clock, IOptions<NotificationOptions> options)
{
    private readonly NotificationOptions _options = options.Value;

    public async Task<EmailMessage?> RenderAsync(Notification n, CancellationToken ct = default)
    {
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == n.ShopId, ct);
        var culture = CultureFor(shop);
        var zone = ShopTime.FindZone(shop.TimeZoneId);

        if (n.Type == NotificationType.OwnerDailyOverview)
            return await RenderOverviewAsync(n, shop, culture, zone, ct);

        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Service)
            .Include(b => b.Payments)
            .SingleAsync(b => b.Id == n.BookingId, ct);

        if (!IsStillRelevant(n.Type, booking, clock.GetUtcNow().UtcDateTime))
            return null;

        var v = Values(shop, booking, culture, zone);
        var to = n.Type is NotificationType.OwnerNewBooking or NotificationType.OwnerCancellation
            ? shop.OwnerEmail
            : booking.CustomerEmail;
        if (string.IsNullOrWhiteSpace(to))
            return null;

        var attachments = n.Type == NotificationType.CustomerConfirmation
            ? new[] { new EmailAttachment("afspraak.ics", "text/calendar", Encoding.UTF8.GetBytes(BuildIcs(shop, booking, culture))) }
            : Array.Empty<EmailAttachment>();

        var key = n.Type.ToString();
        return new EmailMessage(to,
            EmailTexts.Get($"Subject_{key}", culture, v),
            EmailTexts.Get($"Body_{key}", culture, v),
            attachments);
    }

    internal static bool IsStillRelevant(NotificationType type, Booking booking, DateTime nowUtc) => type switch
    {
        NotificationType.CustomerConfirmation or NotificationType.OwnerNewBooking => booking.Status == BookingStatus.Confirmed,
        NotificationType.CustomerReminder => booking.Status == BookingStatus.Confirmed && booking.StartUtc > nowUtc,
        NotificationType.CustomerCancellation or NotificationType.OwnerCancellation => booking.Status == BookingStatus.Cancelled,
        NotificationType.CustomerPaymentFailed => booking.Status == BookingStatus.Cancelled,
        NotificationType.CustomerNoShow => booking.Status == BookingStatus.NoShow,
        _ => false,
    };

    private Dictionary<string, string> Values(Shop shop, Booking booking, CultureInfo culture, TimeZoneInfo zone)
    {
        var start = ShopTime.ToLocal(booking.StartUtc, zone);
        var freeUntilUtc = booking.StartUtc.AddMinutes(-booking.FreeCancellationMinutes);
        var freeUntil = ShopTime.ToLocal(freeUntilUtc, zone);
        var now = clock.GetUtcNow().UtcDateTime;

        var v = new Dictionary<string, string>
        {
            ["ShopName"] = shop.Name,
            ["Service"] = booking.Service!.Name,
            ["Date"] = FormatDate(start, culture),
            ["Time"] = start.ToString("HH:mm", culture),
            ["Address"] = string.IsNullOrWhiteSpace(shop.Address) ? EmailTexts.Get("NoAddress", culture, Empty) : shop.Address,
            ["CustomerName"] = booking.CustomerName,
            ["CustomerEmail"] = booking.CustomerEmail,
            ["CustomerPhoneSuffix"] = string.IsNullOrWhiteSpace(booking.CustomerPhone) ? "" : $", {booking.CustomerPhone}",
            ["Deposit"] = FormatMoney(booking.DepositCents, culture),
            ["CancelUrl"] = _options.CancelUrl(booking.CancelToken),
            ["BookingUrl"] = _options.BookingPageUrl(shop.Slug),
            ["FreeCancelUntil"] = $"{FormatDate(freeUntil, culture)} {freeUntil.ToString("HH:mm", culture)}",
        };

        v["NoShowRule"] = EmailTexts.Get($"NoShowRule_{booking.NoShowPolicy}", culture, v);
        v["CancellationRule"] = EmailTexts.Get(booking.FreeCancellationMinutes > 0 ? "CancellationRule" : "CancellationRule_None", culture, v);
        // Only offer the cancel link in a reminder while cancelling is still free.
        v["CancelLine"] = booking.FreeCancellationMinutes > 0 && now < freeUntilUtc
            ? EmailTexts.Get("ReminderCancelLine", culture, v)
            : "";

        var refunded = booking.Payments.Any(p => p.Status is PaymentStatus.RefundPending or PaymentStatus.Refunded);
        var paid = booking.Payments.Any(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded);
        v["RefundStatus"] = EmailTexts.Get(!paid ? "Refund_NoDeposit" : refunded ? "Refund_Refunded" : "Refund_Kept", culture, v);
        v["OwnerDepositOutcome"] = EmailTexts.Get(!paid ? "Owner_NoDeposit" : refunded ? "Owner_DepositRefunded" : "Owner_DepositKept", culture, v);
        v["NoShowOutcome"] = EmailTexts.Get(booking.NoShowPolicy == NoShowPolicy.RefundDeposit ? "NoShow_Refunded" : "NoShow_Kept", culture, v);
        return v;
    }

    private async Task<EmailMessage?> RenderOverviewAsync(Notification n, Shop shop, CultureInfo culture, TimeZoneInfo zone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shop.OwnerEmail) || n.OverviewDate is not { } day)
            return null;

        var from = ShopTime.ToUtc(day, TimeOnly.MinValue, zone);
        var to = ShopTime.ToUtc(day.AddDays(1), TimeOnly.MinValue, zone);
        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Service)
            .Where(b => b.ShopId == shop.Id && b.Status == BookingStatus.Confirmed && b.StartUtc >= from && b.StartUtc < to)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);
        if (bookings.Count == 0)
            return null; // "skip if none"

        var lines = string.Join("\n", bookings.Select(b => EmailTexts.Get("OverviewLine", culture, new Dictionary<string, string>
        {
            ["Time"] = ShopTime.ToLocal(b.StartUtc, zone).ToString("HH:mm", culture),
            ["Service"] = b.Service!.Name,
            ["CustomerName"] = b.CustomerName,
        })));
        var v = new Dictionary<string, string>
        {
            ["ShopName"] = shop.Name,
            ["Date"] = day.ToString("dddd d MMMM yyyy", culture),
            ["Lines"] = lines,
        };
        return new EmailMessage(shop.OwnerEmail,
            EmailTexts.Get("Subject_OwnerDailyOverview", culture, v),
            EmailTexts.Get("Body_OwnerDailyOverview", culture, v),
            []);
    }

    internal static string BuildIcs(Shop shop, Booking booking, CultureInfo culture)
    {
        static string Utc(DateTime t) => t.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        var summary = EmailTexts.Get("IcsSummary", culture, new Dictionary<string, string>
        {
            ["Service"] = booking.Service!.Name,
            ["ShopName"] = shop.Name,
        });
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Pointer//Bookings//NL",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{booking.Id}@pointer",
            $"DTSTAMP:{Utc(booking.CreatedUtc)}",
            $"DTSTART:{Utc(booking.StartUtc)}",
            $"DTEND:{Utc(booking.EndUtc)}",
            $"SUMMARY:{Escape(summary)}",
        };
        if (!string.IsNullOrWhiteSpace(shop.Address))
            lines.Add($"LOCATION:{Escape(shop.Address)}");
        lines.AddRange(["END:VEVENT", "END:VCALENDAR"]);
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static readonly Dictionary<string, string> Empty = [];

    internal static CultureInfo CultureFor(Shop shop) => shop.Language switch
    {
        "nl" => CultureInfo.GetCultureInfo("nl-BE"),
        var other => CultureInfo.GetCultureInfo(other),
    };

    private static string FormatDate(DateTimeOffset local, CultureInfo culture) => local.ToString("dddd d MMMM yyyy", culture);

    private static string FormatMoney(long cents, CultureInfo culture) =>
        (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("nl-BE"));
}
