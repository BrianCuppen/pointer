namespace Pointer.Core.Domain;

public enum BookingStatus
{
    /// <summary>Slot is held while the customer pays the deposit. Expires at <see cref="Booking.HoldExpiresUtc"/>.</summary>
    PendingPayment,
    Confirmed,
    Cancelled,
    Completed,
    NoShow,
}
