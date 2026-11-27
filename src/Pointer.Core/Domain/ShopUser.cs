namespace Pointer.Core.Domain;

/// <summary>A shop owner's login for the admin. One shop per user in v1.</summary>
public class ShopUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }

    /// <summary>Stored lowercase.</summary>
    public required string Email { get; set; }

    /// <summary>ASP.NET Core Identity password hash (PBKDF2). Never the password.</summary>
    public required string PasswordHash { get; set; }

    public DateTime CreatedUtc { get; set; }
}
