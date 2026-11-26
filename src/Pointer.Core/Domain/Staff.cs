namespace Pointer.Core.Domain;

/// <summary>
/// A person whose time can be booked. In v1 every shop has exactly one implicit staff member,
/// so bookings always reference a staff row and the overlap constraint stays per staff member.
/// </summary>
public class Staff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Shop? Shop { get; set; }

    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}
