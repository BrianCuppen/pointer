namespace Pointer.Core.Domain;

public enum PaymentStatus
{
    /// <summary>Checkout started, not paid yet.</summary>
    Pending,

    /// <summary>Money is on the shop's connected account.</summary>
    Succeeded,

    /// <summary>Checkout expired or the payment failed. No money moved.</summary>
    Failed,

    /// <summary>We asked the provider for a refund and wait for its confirmation.</summary>
    RefundPending,

    /// <summary>The provider confirmed the refund.</summary>
    Refunded,
}
