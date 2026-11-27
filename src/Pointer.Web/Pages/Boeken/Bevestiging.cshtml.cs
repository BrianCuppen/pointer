using Microsoft.AspNetCore.Mvc;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Boeken;

/// <summary>
/// Where Stripe sends the customer back. The webhook decides the outcome; while it has not arrived
/// the page polls (htmx, every 3 seconds). The secret token in the URL is the only access key.
/// </summary>
public class BevestigingModel(PublicBookingQueries queries, BookingLifecycleService lifecycle) : BookingPageModel(queries)
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";

    public Booking Booking { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadShopAsync(ct))
            return NotFound();
        var booking = await lifecycle.FindByCancelTokenAsync(Token, ct);
        if (booking is null || booking.ShopId != Shop.Id)
            return NotFound();
        Booking = booking;
        Response.Headers.CacheControl = "no-store";
        return Page();
    }
}
