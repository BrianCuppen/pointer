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

    // Notifications (docs/notifications-decisions.md).

    /// <summary>Where owner emails go. No owner emails are sent while this is empty.</summary>
    public string? OwnerEmail { get; set; }

    /// <summary>Shown in customer emails and the calendar attachment.</summary>
    public string? Address { get; set; }

    /// <summary>Email the owner on each new booking and each cancellation by a customer.</summary>
    public bool OwnerInstantEmails { get; set; } = true;

    /// <summary>Email the owner today's appointments at 07:00 local time.</summary>
    public bool OwnerDailyOverview { get; set; }

    /// <summary>Customer reminder this many hours before the start. 2 to 48.</summary>
    public int ReminderHoursBefore { get; set; } = 24;

    /// <summary>Tell the customer when a booking is marked as no-show.</summary>
    public bool NoShowNoticeEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; }

    public List<Service> Services { get; set; } = [];
    public List<Staff> Staff { get; set; } = [];
}
