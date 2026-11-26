namespace Pointer.Core.Bookings;

public sealed class BookingOptions
{
    public const string SectionName = "Bookings";

    /// <summary>How long a slot stays reserved while the customer pays the deposit.</summary>
    public int PaymentHoldMinutes { get; set; } = 15;
}
