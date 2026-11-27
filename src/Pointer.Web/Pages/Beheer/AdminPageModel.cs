using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;
using Pointer.Web.Auth;

namespace Pointer.Web.Pages.Beheer;

/// <summary>Base for admin pages: the shop id always comes from the owner's sign-in cookie.</summary>
public abstract class AdminPageModel(IStringLocalizer<SharedResource> t) : PageModel
{
    protected IStringLocalizer<SharedResource> T { get; } = t;

    protected Guid ShopId => User.ShopId();

    protected void AddErrors(AdminResult result)
    {
        foreach (var e in result.Errors)
            ModelState.AddModelError(e.Field, T[e.MessageKey]);
    }
}
