namespace Pointer.Core.Payments;

public sealed record DepositCheckoutRequest(
    string ConnectedAccountId,
    Guid BookingId,
    long AmountCents,
    long ApplicationFeeCents,
    string Currency,
    string Description,
    string CustomerEmail,
    DateTime ExpiresUtc,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey);

public sealed record DepositCheckout(string SessionId, string Url);

/// <summary>
/// The payment provider. Every call acts on the shop's connected account: the charge is created
/// there and our fee is an application fee, so customer money never passes through our account.
/// </summary>
public interface IPaymentGateway
{
    string Provider { get; }

    Task<DepositCheckout> CreateDepositCheckoutAsync(DepositCheckoutRequest request, CancellationToken ct);

    /// <summary>Full refund of a deposit, including our application fee. Safe to repeat with the same key.</summary>
    Task RefundAsync(string connectedAccountId, string paymentIntentId, string idempotencyKey, CancellationToken ct);
}
