using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages;

/// <summary>The customer's cancel link from the emails. The token in the URL is the authorization.</summary>
public class AnnulerenModel(BookingLifecycleService lifecycle, PublicBookingQueries queries, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";

    public Booking Booking { get; private set; } = null!;
    public string ShopName { get; private set; } = "";
    public bool CanCancel { get; private set; }
    public bool WouldRefund { get; private set; }
    public bool HasDeposit { get; private set; }
    public bool Done { get; private set; }
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public DateTimeOffset Local(DateTime utc) => ShopTime.ToLocal(utc, _zone);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await lifecycle.CancelByCustomerAsync(Token, ct);
        if (result == LifecycleResult.NotFound || !await LoadAsync(ct))
            return NotFound();
        Done = result == LifecycleResult.Done;
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        var booking = await lifecycle.FindByCancelTokenAsync(Token, ct);
        if (booking is null)
            return false;
        var shop = (await queries.FindShopByIdAsync(booking.ShopId, ct))!;
        Booking = booking;
        ShopName = shop.Name;
        _zone = ShopTime.FindZone(shop.TimeZoneId);
        CanCancel = booking.Status is BookingStatus.Confirmed or BookingStatus.PendingPayment && booking.StartUtc > clock.GetUtcNow().UtcDateTime;
        WouldRefund = lifecycle.WouldRefundCustomerCancellation(booking);
        HasDeposit = booking.Payments.Any(p => p.Status == PaymentStatus.Succeeded);
        Response.Headers.CacheControl = "no-store";
        return true;
    }
}
