using Pointer.Core.Payments;

namespace Pointer.Tests;

public sealed class FakePaymentGateway : IPaymentGateway
{
    public string Provider => "stripe";

    public List<DepositCheckoutRequest> Checkouts { get; } = [];
    public List<(string Account, string PaymentIntentId, string IdempotencyKey)> Refunds { get; } = [];
    public bool FailRefunds { get; set; }

    public Task<DepositCheckout> CreateDepositCheckoutAsync(DepositCheckoutRequest request, CancellationToken ct)
    {
        Checkouts.Add(request);
        var id = $"cs_test_{request.BookingId:N}";
        return Task.FromResult(new DepositCheckout(id, $"https://checkout.stripe.test/{id}"));
    }

    public Task RefundAsync(string connectedAccountId, string paymentIntentId, string idempotencyKey, CancellationToken ct)
    {
        if (FailRefunds)
            throw new HttpRequestException("Stripe unavailable");
        lock (Refunds)
            Refunds.Add((connectedAccountId, paymentIntentId, idempotencyKey));
        return Task.CompletedTask;
    }
}
