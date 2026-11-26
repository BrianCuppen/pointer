namespace Pointer.Core.Domain;

/// <summary>
/// A deposit payment. The money is charged on the shop's connected account (Stripe Connect,
/// direct charge) and our fee is taken as an application fee. We never hold customer money.
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>Payment provider, for example <c>stripe</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>The provider's id for this payment (Stripe PaymentIntent or Checkout Session id).</summary>
    public required string ProviderPaymentId { get; set; }

    public long AmountCents { get; set; }
    public long PlatformFeeCents { get; set; }
    public string Currency { get; set; } = "EUR";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
