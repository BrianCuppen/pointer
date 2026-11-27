using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;

namespace Pointer.Web.Auth;

/// <summary>Cookie sign-in for shop owners. Passwords are hashed with ASP.NET Core Identity's PBKDF2 hasher.</summary>
public sealed class OwnerAuth(PointerDbContext db, IPasswordHasher<ShopUser> hasher)
{
    public const string ShopIdClaim = "shop_id";
    public const string LoginRateLimit = "login";

    public async Task<ShopUser?> ValidateAsync(string email, string password, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.ShopUsers.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            // Same work for unknown emails, so response time does not reveal which emails exist.
            hasher.HashPassword(new ShopUser { Email = normalized, PasswordHash = "" }, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    public static Task SignInAsync(HttpContext http, ShopUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ShopIdClaim, user.ShopId.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public string HashPassword(string password) => hasher.HashPassword(new ShopUser { Email = "", PasswordHash = "" }, password);
}

public static class OwnerClaims
{
    /// <summary>The signed-in owner's shop. Admin pages only ever read and write this shop.</summary>
    public static Guid ShopId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(OwnerAuth.ShopIdClaim) ?? throw new InvalidOperationException("Not signed in as an owner."));
}
