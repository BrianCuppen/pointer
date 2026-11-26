namespace Pointer.Core.Availability;

public sealed class AvailabilityOptions
{
    public const string SectionName = "Availability";

    /// <summary>Distance between offered start times.</summary>
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>A slot must start at least this long from now.</summary>
    public int MinimumNoticeMinutes { get; set; } = 60;

    /// <summary>How far ahead customers can book.</summary>
    public int BookingHorizonDays { get; set; } = 60;
}
