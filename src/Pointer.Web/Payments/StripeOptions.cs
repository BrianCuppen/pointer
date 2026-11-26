namespace Pointer.Web.Payments;

/// <summary>From environment variables only (Stripe__SecretKey, Stripe__WebhookSecret). Never commit these.</summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Platform secret key. Use sk_test_... everywhere except production.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Signing secret (whsec_...) of the Connect webhook endpoint that receives connected-account events.</summary>
    public string? WebhookSecret { get; set; }
}
