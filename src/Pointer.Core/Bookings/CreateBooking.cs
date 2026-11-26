using Pointer.Core.Domain;

namespace Pointer.Core.Bookings;

public sealed record CreateBookingRequest(
    Guid ShopId,
    Guid ServiceId,
    Guid StaffId,
    DateTime StartUtc,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone);

public abstract record CreateBookingResult
{
    private CreateBookingResult() { }

    public sealed record Created(Booking Booking) : CreateBookingResult;

    /// <summary>Another booking overlaps this slot. The database refused it.</summary>
    public sealed record SlotTaken : CreateBookingResult;

    public sealed record Invalid(string Reason) : CreateBookingResult;
}
