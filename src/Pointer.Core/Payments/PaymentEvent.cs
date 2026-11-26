namespace Pointer.Core.Payments;

public enum PaymentEventKind
{
    /// <summary>The customer paid. Money is on the connected account.</summary>
    CheckoutPaid,

    /// <summary>The checkout expired or the payment failed. No money moved.</summary>
    CheckoutFailed,

    /// <summary>The charge was refunded in full.</summary>
    Refunded,
}

/// <summary>
/// A provider event reduced to what the booking logic needs. The Stripe-specific parsing and
/// signature check happen in the web layer; everything here can be tested without Stripe.
/// </summary>
/// <param name="ProviderPaymentId">Checkout Session id. Null for refund events, which carry only <paramref name="PaymentIntentId"/>.</param>
public sealed record PaymentEvent(
    string Provider,
    string EventId,
    string Type,
    PaymentEventKind Kind,
    string? ProviderPaymentId,
    string? PaymentIntentId,
    string? ConnectedAccountId);
