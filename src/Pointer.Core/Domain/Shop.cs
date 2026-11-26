namespace Pointer.Core.Domain;

/// <summary>A tenant: one business with its own booking page.</summary>
public class Shop
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Lowercase, URL-safe identifier used in <c>{slug}.&lt;domain&gt;</c>.</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>IANA time zone id, for example <c>Europe/Brussels</c>.</summary>
    public string TimeZoneId { get; set; } = "Europe/Brussels";

    /// <summary>Language for customer-facing text (BCP 47 primary tag).</summary>
    public string Language { get; set; } = "nl";

    /// <summary>The shop's Stripe Connect account (acct_...). Null until onboarding is done.</summary>
    public string? PaymentAccountId { get; set; }

    public NoShowPolicy NoShowPolicy { get; set; } = NoShowPolicy.KeepDeposit;

    /// <summary>How long before the start a customer can still cancel for free.</summary>
    public int FreeCancellationMinutes { get; set; } = 24 * 60;

    public DateTime CreatedUtc { get; set; }

    public List<Service> Services { get; set; } = [];
    public List<Staff> Staff { get; set; } = [];
}
