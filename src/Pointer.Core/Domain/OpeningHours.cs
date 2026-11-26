namespace Pointer.Core.Domain;

/// <summary>
/// A weekly recurring open interval in the shop's local time. A day can have several rows
/// (for example a lunch break). <see cref="StaffId"/> null means the whole shop.
/// </summary>
public class OpeningHours
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid? StaffId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Local wall-clock time in the shop's time zone.</summary>
    public TimeOnly Opens { get; set; }

    /// <summary>Local wall-clock time in the shop's time zone. Must be after <see cref="Opens"/>.</summary>
    public TimeOnly Closes { get; set; }
}
