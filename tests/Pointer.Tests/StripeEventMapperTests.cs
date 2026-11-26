using System.Security.Cryptography;
using System.Text;
using Pointer.Core.Payments;
using Pointer.Web.Payments;
using Stripe;

namespace Pointer.Tests;

public sealed class StripeEventMapperTests
{
    private const string Secret = "whsec_test_secret";

    private static string Sign(string payload, string secret = Secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    private static string EventJson(string type, string dataObject) => $$$"""
        {"id":"evt_1","object":"event","api_version":"2024-06-20","created":1764000000,"livemode":false,
         "account":"acct_shop","type":"{{{type}}}","data":{"object":{{{dataObject}}}}}
        """;

    private static Event Parse(string json) =>
        EventUtility.ConstructEvent(json, Sign(json), Secret, throwOnApiVersionMismatch: false);

    [Fact]
    public void Paid_checkout_maps_to_checkout_paid()
    {
        var evt = StripeEventMapper.Map(Parse(EventJson("checkout.session.completed",
            """{"id":"cs_1","object":"checkout.session","payment_status":"paid","payment_intent":"pi_1"}""")));

        Assert.Equal(new PaymentEvent("stripe", "evt_1", "checkout.session.completed", PaymentEventKind.CheckoutPaid, "cs_1", "pi_1", "acct_shop"), evt);
    }

    [Fact]
    public void Completed_but_unpaid_checkout_is_not_acted_on()
    {
        Assert.Null(StripeEventMapper.Map(Parse(EventJson("checkout.session.completed",
            """{"id":"cs_1","object":"checkout.session","payment_status":"unpaid"}"""))));
    }

    [Theory]
    [InlineData("checkout.session.expired")]
    [InlineData("checkout.session.async_payment_failed")]
    public void Expired_or_failed_checkout_maps_to_checkout_failed(string type)
    {
        var evt = StripeEventMapper.Map(Parse(EventJson(type, """{"id":"cs_1","object":"checkout.session","payment_status":"unpaid"}""")));

        Assert.Equal(PaymentEventKind.CheckoutFailed, evt!.Kind);
        Assert.Equal("cs_1", evt.ProviderPaymentId);
    }

    [Fact]
    public void Full_refund_maps_by_payment_intent_and_partial_refund_is_ignored()
    {
        var full = StripeEventMapper.Map(Parse(EventJson("charge.refunded",
            """{"id":"ch_1","object":"charge","refunded":true,"payment_intent":"pi_1"}""")));
        var partial = StripeEventMapper.Map(Parse(EventJson("charge.refunded",
            """{"id":"ch_1","object":"charge","refunded":false,"payment_intent":"pi_1"}""")));

        Assert.Equal(PaymentEventKind.Refunded, full!.Kind);
        Assert.Null(full.ProviderPaymentId);
        Assert.Equal("pi_1", full.PaymentIntentId);
        Assert.Null(partial);
    }

    [Fact]
    public void Wrong_signature_is_rejected()
    {
        var json = EventJson("checkout.session.completed", """{"id":"cs_1","object":"checkout.session"}""");

        Assert.Throws<StripeException>(() =>
            EventUtility.ConstructEvent(json, Sign(json, "whsec_other"), Secret, throwOnApiVersionMismatch: false));
    }

    [Fact]
    public void Checkout_offers_bancontact_ideal_and_card()
    {
        Assert.Equal(["bancontact", "ideal", "card"], StripePaymentGateway.PaymentMethodTypes);
    }
}
