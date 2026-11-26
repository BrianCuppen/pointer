namespace Pointer.Core.Domain;

/// <summary>A closed period (day off, holiday). <see cref="StaffId"/> null means the whole shop.</summary>
public class TimeOff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid? StaffId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public string? Note { get; set; }
}
