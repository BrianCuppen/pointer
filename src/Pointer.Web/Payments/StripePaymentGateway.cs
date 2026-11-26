using Microsoft.Extensions.Options;
using Pointer.Core.Payments;
using Stripe;
using Stripe.Checkout;

namespace Pointer.Web.Payments;

/// <summary>
/// Stripe Connect with direct charges: the Checkout Session is created on the shop's connected
/// account (Stripe-Account header) and our fee is taken as an application fee. The customer's
/// money goes straight to the shop (hard rule 1).
/// </summary>
public sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    public const string ProviderName = "stripe";

    // Hard rule 2: Bancontact, iDEAL and cards. This filters Stripe's dynamic payment methods, so they
    // must also be enabled for connected accounts in the Stripe dashboard (Settings > Payment methods > Connect).
    internal static readonly string[] PaymentMethodTypes = ["bancontact", "ideal", "card"];

    public string Provider => ProviderName;

    private StripeClient Client => new(options.Value.SecretKey
        ?? throw new InvalidOperationException("Stripe is not configured. Set the Stripe__SecretKey environment variable."));

    public async Task<DepositCheckout> CreateDepositCheckoutAsync(DepositCheckoutRequest request, CancellationToken ct)
    {
        var bookingId = request.BookingId.ToString();
        var create = new SessionCreateOptions
        {
            Mode = "payment",
            AllowedPaymentMethodTypes = [.. PaymentMethodTypes],
            Locale = "auto",
            CustomerEmail = request.CustomerEmail,
            ClientReferenceId = bookingId,
            ExpiresAt = request.ExpiresUtc,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = request.AmountCents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.Description },
                    },
                },
            ],
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = request.ApplicationFeeCents,
                Metadata = new Dictionary<string, string> { ["booking_id"] = bookingId },
            },
            Metadata = new Dictionary<string, string> { ["booking_id"] = bookingId },
        };

        var session = await new SessionService(Client).CreateAsync(create, new RequestOptions
        {
            StripeAccount = request.ConnectedAccountId,
            IdempotencyKey = request.IdempotencyKey,
        }, ct);

        return new DepositCheckout(session.Id, session.Url);
    }

    public async Task RefundAsync(string connectedAccountId, string paymentIntentId, string idempotencyKey, CancellationToken ct)
    {
        await new RefundService(Client).CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            RefundApplicationFee = true,
        }, new RequestOptions { StripeAccount = connectedAccountId, IdempotencyKey = idempotencyKey }, ct);
    }
}
