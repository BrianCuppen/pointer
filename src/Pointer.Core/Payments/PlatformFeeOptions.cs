namespace Pointer.Core.Payments;

public sealed class PlatformFeeOptions
{
    public const string SectionName = "Fees";

    /// <summary>Our application fee per paid deposit, in euro cents. Never more than the deposit itself.</summary>
    public long PerDepositFeeCents { get; set; } = 30;

    public long FeeFor(long depositCents) => Math.Clamp(PerDepositFeeCents, 0, depositCents);
}
