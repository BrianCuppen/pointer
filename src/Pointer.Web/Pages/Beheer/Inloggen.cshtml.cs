using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Pointer.Web.Auth;

namespace Pointer.Web.Pages.Beheer;

[AllowAnonymous]
[EnableRateLimiting(OwnerAuth.LoginRateLimit)]
public class InloggenModel(OwnerAuth auth, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await auth.ValidateAsync(Email, Password, ct);
        if (user is null)
        {
            ModelState.AddModelError("", t["Login_Failed"]);
            return Page();
        }

        await OwnerAuth.SignInAsync(HttpContext, user);
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Beheer/Index");
    }
}
