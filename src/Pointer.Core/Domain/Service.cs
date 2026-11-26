namespace Pointer.Core.Domain;

/// <summary>Something a customer can book, for example "Haircut, 30 min".</summary>
public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Shop? Shop { get; set; }

    public required string Name { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>Full price in euro cents. Informational: only the deposit is paid online.</summary>
    public long PriceCents { get; set; }

    /// <summary>Deposit paid online when booking, in euro cents. 0 means no deposit.</summary>
    public long DepositCents { get; set; }

    public bool IsActive { get; set; } = true;
}
