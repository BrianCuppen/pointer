using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pointer.Core.Availability;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Core.Bookings;

/// <summary>
/// Creates bookings. Double bookings are prevented by the database exclusion constraint
/// <see cref="PointerDbContext.BookingOverlapConstraint"/>, not by a check-then-insert in code,
/// so two customers racing for the same slot can never both get it.
/// </summary>
public sealed class BookingService(
    PointerDbContext db,
    AvailabilityService availability,
    TimeProvider clock,
    IOptions<BookingOptions> options)
{
    private readonly BookingOptions _options = options.Value;

    /// <summary>
    /// Holds a slot in <see cref="BookingStatus.PendingPayment"/> until the deposit is paid
    /// or the hold expires. The slot must lie inside opening hours and outside time off.
    /// </summary>
    public async Task<CreateBookingResult> CreatePendingAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        if (request.StartUtc.Kind != DateTimeKind.Utc)
            return new CreateBookingResult.Invalid("start_must_be_utc");

        var now = clock.GetUtcNow().UtcDateTime;
        if (request.StartUtc <= now)
            return new CreateBookingResult.Invalid("start_in_past");

        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ShopId, ct);
        if (shop is null)
            return new CreateBookingResult.Invalid("shop_not_found");

        var service = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.ShopId == shop.Id && s.IsActive, ct);
        if (service is null)
            return new CreateBookingResult.Invalid("service_not_found");

        var staffExists = await db.Staff.AnyAsync(s => s.Id == request.StaffId && s.ShopId == shop.Id && s.IsActive, ct);
        if (!staffExists)
            return new CreateBookingResult.Invalid("staff_not_found");

        if (!await availability.IsWithinScheduleAsync(shop, service, request.StaffId, request.StartUtc, ct))
            return new CreateBookingResult.Invalid("outside_schedule");

        var start = request.StartUtc;
        var end = start.AddMinutes(service.DurationMinutes);

        var booking = new Booking
        {
            ShopId = shop.Id,
            ServiceId = service.Id,
            StaffId = request.StaffId,
            StartUtc = start,
            EndUtc = end,
            Status = BookingStatus.PendingPayment,
            HoldExpiresUtc = now.AddMinutes(_options.PaymentHoldMinutes),
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
            DepositCents = service.DepositCents,
            NoShowPolicy = shop.NoShowPolicy,
            FreeCancellationMinutes = shop.FreeCancellationMinutes,
            CreatedUtc = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Release expired payment holds on this slot so an abandoned checkout does not block it.
        await db.Bookings
            .Where(b => b.StaffId == request.StaffId
                        && b.Status == BookingStatus.PendingPayment
                        && b.HoldExpiresUtc < now
                        && b.StartUtc < end && b.EndUtc > start)
            .ExecuteUpdateAsync(u => u
                .SetProperty(b => b.Status, BookingStatus.Cancelled)
                .SetProperty(b => b.HoldExpiresUtc, (DateTime?)null)
                .SetProperty(b => b.CancelledUtc, now), ct);

        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsOverlapViolation(ex))
        {
            db.Entry(booking).State = EntityState.Detached;
            await tx.RollbackAsync(ct);
            return new CreateBookingResult.SlotTaken();
        }

        return new CreateBookingResult.Created(booking);
    }

    internal static bool IsOverlapViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } pg
        && pg.ConstraintName == PointerDbContext.BookingOverlapConstraint;
}
