namespace Pointer.Core.Bookings;

public sealed class BookingOptions
{
    public const string SectionName = "Bookings";

    /// <summary>How long a slot stays reserved while the customer pays the deposit. Stripe Checkout needs at least 30 minutes.</summary>
    public int PaymentHoldMinutes { get; set; } = 30;
}
