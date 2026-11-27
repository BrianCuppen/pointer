using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Pointer.Core.Admin;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Notifications;

namespace Pointer.Web.Pages.Beheer;

/// <summary>The owner's week calendar, with cancel, completed and no-show actions.</summary>
public class IndexModel(
    ShopAdminService admin,
    CalendarQueries calendar,
    BookingLifecycleService lifecycle,
    IOptions<NotificationOptions> links,
    TimeProvider clock,
    IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{

    public string ShopName { get; private set; } = "";
    public string BookingPageUrl { get; private set; } = "";
    public string EmbedSnippet { get; private set; } = "";
    public DateOnly Monday { get; private set; }
    public DateOnly Today { get; private set; }
    public IReadOnlyList<CalendarDay> Week { get; private set; } = [];

    public async Task OnGetAsync([FromQuery] DateOnly? week, CancellationToken ct)
    {
        var shop = await admin.GetShopAsync(ShopId, ct);
        ShopName = shop.Name;
        BookingPageUrl = links.Value.BookingPageUrl(shop.Slug);
        EmbedSnippet = $"<script src=\"{links.Value.PublicBaseUrl}/embed.js\" data-shop=\"{shop.Slug}\" async></script>";

        var today = ShopTime.LocalDate(clock.GetUtcNow().UtcDateTime, ShopTime.FindZone(shop.TimeZoneId));
        Today = today;
        Monday = CalendarQueries.MondayOf(week ?? today);
        Week = await calendar.WeekAsync(shop, Monday, ct);
    }

    public Task<IActionResult> OnPostCancelAsync(Guid id, DateOnly? week, CancellationToken ct) =>
        ActAsync(() => lifecycle.CancelByShopAsync(ShopId, id, ct), "Calendar_Cancelled", week);

    public Task<IActionResult> OnPostNoShowAsync(Guid id, DateOnly? week, CancellationToken ct) =>
        ActAsync(() => lifecycle.MarkNoShowAsync(ShopId, id, ct), "Calendar_MarkedNoShow", week);

    public Task<IActionResult> OnPostCompletedAsync(Guid id, DateOnly? week, CancellationToken ct) =>
        ActAsync(() => lifecycle.MarkCompletedAsync(ShopId, id, ct), "Calendar_MarkedCompleted", week);

    private async Task<IActionResult> ActAsync(Func<Task<LifecycleResult>> action, string doneKey, DateOnly? week)
    {
        var result = await action();
        if (result == LifecycleResult.NotFound)
            return NotFound();
        TempData["Saved"] = T[result == LifecycleResult.Done ? doneKey : "Calendar_NotAllowed"].Value;
        return RedirectToPage(new { week = week?.ToString("yyyy-MM-dd") });
    }
}
