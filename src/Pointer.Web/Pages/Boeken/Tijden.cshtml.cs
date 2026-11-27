using Microsoft.AspNetCore.Mvc;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;

namespace Pointer.Web.Pages.Boeken;

/// <summary>htmx partial: the open start times of one day. Always read live.</summary>
public class TijdenModel(PublicBookingQueries queries, AvailabilityService availability) : BookingPageModel(queries)
{
    public SlotsView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync([FromQuery] Guid service, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!await LoadShopAsync(ct))
            return NotFound();
        var s = await Queries.ActiveServiceAsync(Shop.Id, service, ct);
        if (s is null)
            return NotFound();

        View = await SlotsView.LoadAsync(Queries, availability, Shop, s, date, Zone, ct);
        Response.Headers.CacheControl = "no-store";
        return Page();
    }
}
