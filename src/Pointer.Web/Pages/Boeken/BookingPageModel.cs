using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Boeken;

/// <summary>Shared loading for the public booking pages: the shop comes from the slug in the URL.</summary>
public abstract class BookingPageModel(PublicBookingQueries queries) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Slug { get; set; } = "";

    public Shop Shop { get; private set; } = null!;
    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    protected PublicBookingQueries Queries => queries;

    protected async Task<bool> LoadShopAsync(CancellationToken ct)
    {
        var shop = await queries.FindShopAsync(Slug, ct);
        if (shop is null)
            return false;
        Shop = shop;
        Zone = ShopTime.FindZone(shop.TimeZoneId);
        return true;
    }

    public DateTimeOffset Local(DateTime utc) => ShopTime.ToLocal(utc, Zone);
}
