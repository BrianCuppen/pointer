using Pointer.Core.Payments;

namespace Pointer.Web.Payments;

/// <summary>
/// Stands in for Stripe when <c>Payments__Mode=demo</c>, so the whole booking flow can be clicked
/// through without any payment account. No money moves: the "checkout" is a local page
/// (<c>/demo-betaling/{id}</c>) whose buttons feed the same <see cref="PaymentWebhookProcessor"/>
/// events Stripe would send. Never enable this where real customers book.
/// </summary>
public sealed class DemoPaymentGateway(ILogger<DemoPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "demo";

    public string Provider => ProviderName;

    public Task<DepositCheckout> CreateDepositCheckoutAsync(DepositCheckoutRequest request, CancellationToken ct)
    {
        var sessionId = $"demo_cs_{request.BookingId:N}";
        return Task.FromResult(new DepositCheckout(sessionId, $"/demo-betaling/{sessionId}"));
    }

    public Task RefundAsync(string connectedAccountId, string paymentIntentId, string idempotencyKey, CancellationToken ct)
    {
        logger.LogInformation("Demo payments: refund of {PaymentIntentId} recorded, no money moved", paymentIntentId);
        return Task.CompletedTask;
    }
}
