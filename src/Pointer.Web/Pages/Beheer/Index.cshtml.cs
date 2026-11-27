using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Pointer.Core.Admin;
using Pointer.Core.Notifications;

namespace Pointer.Web.Pages.Beheer;

public class IndexModel(ShopAdminService admin, IOptions<NotificationOptions> links, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    public string ShopName { get; private set; } = "";
    public string BookingPageUrl { get; private set; } = "";
    public string EmbedSnippet { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var shop = await admin.GetShopAsync(ShopId, ct);
        ShopName = shop.Name;
        BookingPageUrl = links.Value.BookingPageUrl(shop.Slug);
        EmbedSnippet = $"<script src=\"{links.Value.PublicBaseUrl}/embed.js\" data-shop=\"{shop.Slug}\" async></script>";
    }
}
