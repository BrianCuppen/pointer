using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Admin;

public sealed record CustomerBooking(
    Guid BookingId, DateTimeOffset StartLocal, string Service, BookingStatus Status, long DepositCents,
    string CustomerName, string CustomerEmail, string? CustomerPhone, DateTime CreatedUtc,
    IReadOnlyList<CustomerPayment> Payments);

public sealed record CustomerPayment(string Provider, long AmountCents, PaymentStatus Status, DateTime CreatedUtc);

public enum CustomerDeleteResult { Deleted, NotFound, HasUpcomingBookings }

/// <summary>
/// GDPR requests from a shop's customer, handled by the shop owner: export everything stored about
/// an email address, or erase it. Erasure anonymises the bookings instead of deleting them, so the
/// shop's payment records (needed for its accounting) keep their amounts but lose the person.
/// </summary>
public sealed class CustomerDataService(PointerDbContext db, TimeProvider clock)
{
    public const string ErasedName = "";
    public const string ErasedEmail = "";

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();

    private IQueryable<Booking> BookingsOf(Guid shopId, string email)
    {
        var e = Normalise(email);
        return db.Bookings.Where(b => b.ShopId == shopId && b.CustomerEmail.ToLower() == e);
    }

    public async Task<IReadOnlyList<CustomerBooking>> FindAsync(Guid shopId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return [];
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == shopId, ct);
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        var bookings = await BookingsOf(shopId, email).AsNoTracking()
            .Include(b => b.Service).Include(b => b.Payments)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);

        return bookings.Select(b => new CustomerBooking(
            b.Id, ShopTime.ToLocal(b.StartUtc, zone), b.Service!.Name, b.Status, b.DepositCents,
            b.CustomerName, b.CustomerEmail, b.CustomerPhone, b.CreatedUtc,
            b.Payments.OrderBy(p => p.CreatedUtc)
                .Select(p => new CustomerPayment(p.Provider, p.AmountCents, p.Status, p.CreatedUtc)).ToList())).ToList();
    }

    /// <summary>A machine-readable export (GDPR art. 15 and 20).</summary>
    public async Task<string?> ExportJsonAsync(Guid shopId, string email, CancellationToken ct = default)
    {
        var bookings = await FindAsync(shopId, email, ct);
        if (bookings.Count == 0)
            return null;
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == shopId, ct);
        return JsonSerializer.Serialize(new
        {
            exportedUtc = clock.GetUtcNow().UtcDateTime,
            shop = shop.Name,
            email = Normalise(email),
            bookings,
        }, ExportJson);
    }

    /// <summary>
    /// Erases the customer's personal data (GDPR art. 17). Refused while they still have an upcoming
    /// booking, so the owner cancels that first and the customer gets the cancellation email.
    /// </summary>
    public async Task<CustomerDeleteResult> EraseAsync(Guid shopId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return CustomerDeleteResult.NotFound;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var bookings = await BookingsOf(shopId, email).ToListAsync(ct);
        if (bookings.Count == 0)
            return CustomerDeleteResult.NotFound;

        var upcoming = bookings.Any(b => b.EndUtc > now
            && (b.Status == BookingStatus.Confirmed
                || (b.Status == BookingStatus.PendingPayment && b.HoldExpiresUtc > now)));
        if (upcoming)
            return CustomerDeleteResult.HasUpcomingBookings;

        foreach (var b in bookings)
        {
            b.CustomerName = ErasedName;
            b.CustomerEmail = ErasedEmail;
            b.CustomerPhone = null;
        }
        await db.SaveChangesAsync(ct);

        var ids = bookings.Select(b => b.Id).ToList();
        await db.Notifications.Where(n => n.BookingId != null && ids.Contains(n.BookingId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Recipient, (string?)null).SetProperty(n => n.LastError, (string?)null), ct);
        await db.Notifications.Where(n => n.BookingId != null && ids.Contains(n.BookingId.Value) && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Skipped), ct);

        await tx.CommitAsync(ct);
        return CustomerDeleteResult.Deleted;
    }

    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
