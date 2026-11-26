namespace Pointer.Core.Domain;

/// <summary>What happens to the deposit when a customer does not show up.</summary>
public enum NoShowPolicy
{
    /// <summary>The shop keeps the deposit (default).</summary>
    KeepDeposit,

    /// <summary>The deposit is refunded to the customer.</summary>
    RefundDeposit,
}
