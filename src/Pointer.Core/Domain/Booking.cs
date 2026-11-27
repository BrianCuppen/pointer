using System.Security.Cryptography;

namespace Pointer.Core.Domain;

public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ShopId { get; set; }
    public Guid ServiceId { get; set; }
    public Service? Service { get; set; }
    public Guid StaffId { get; set; }
    public Staff? Staff { get; set; }

    /// <summary>Start, inclusive. Always UTC.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>End, exclusive. Always UTC. Back-to-back bookings do not overlap.</summary>
    public DateTime EndUtc { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;

    /// <summary>Only set while <see cref="BookingStatus.PendingPayment"/>. After this the hold may be released.</summary>
    public DateTime? HoldExpiresUtc { get; set; }

    // Customer contact details: keep this minimal (GDPR).
    public required string CustomerName { get; set; }
    public required string CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    // Snapshot of the rules shown to the customer before payment. Later changes to the
    // shop's policy must not change the deal for bookings that already exist.
    public long DepositCents { get; set; }
    public NoShowPolicy NoShowPolicy { get; set; }
    public int FreeCancellationMinutes { get; set; }

    /// <summary>Secret for the customer's cancel link. Possession of the link is the authorization.</summary>
    public string CancelToken { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public DateTime CreatedUtc { get; set; }
    public DateTime? CancelledUtc { get; set; }

    public List<Payment> Payments { get; set; } = [];
}
