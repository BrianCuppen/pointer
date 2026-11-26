using Pointer.Core.Payments;
using Stripe;
using Stripe.Checkout;

namespace Pointer.Web.Payments;

/// <summary>Turns a verified Stripe event into a <see cref="PaymentEvent"/>, or null for events we do not act on.</summary>
public static class StripeEventMapper
{
    public static PaymentEvent? Map(Event stripeEvent)
    {
        PaymentEvent Make(PaymentEventKind kind, string? sessionId, string? paymentIntentId) =>
            new(StripePaymentGateway.ProviderName, stripeEvent.Id, stripeEvent.Type, kind, sessionId, paymentIntentId, stripeEvent.Account);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted when stripeEvent.Data.Object is Session s:
                // Card, Bancontact and iDEAL are paid at completion. Anything else waits for async_payment_*.
                return s.PaymentStatus == "paid" ? Make(PaymentEventKind.CheckoutPaid, s.Id, s.PaymentIntentId) : null;

            case EventTypes.CheckoutSessionAsyncPaymentSucceeded when stripeEvent.Data.Object is Session s:
                return Make(PaymentEventKind.CheckoutPaid, s.Id, s.PaymentIntentId);

            case EventTypes.CheckoutSessionAsyncPaymentFailed when stripeEvent.Data.Object is Session s:
                return Make(PaymentEventKind.CheckoutFailed, s.Id, s.PaymentIntentId);

            case EventTypes.CheckoutSessionExpired when stripeEvent.Data.Object is Session s:
                return Make(PaymentEventKind.CheckoutFailed, s.Id, s.PaymentIntentId);

            case EventTypes.ChargeRefunded when stripeEvent.Data.Object is Charge c:
                // Only a full refund ends the deposit. Partial refunds are not part of the product.
                return c.Refunded ? Make(PaymentEventKind.Refunded, null, c.PaymentIntentId) : null;

            default:
                return null;
        }
    }
}
