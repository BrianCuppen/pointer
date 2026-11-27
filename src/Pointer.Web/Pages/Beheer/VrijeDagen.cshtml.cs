using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;
using Pointer.Core.Availability;

namespace Pointer.Web.Pages.Beheer;

public class VrijeDagenModel(ShopAdminService admin, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    [BindProperty] public TimeOffForm Input { get; set; } = new();

    public List<(Guid Id, DateOnly From, DateOnly To, string? Note)> Upcoming { get; private set; } = [];

    public sealed class TimeOffForm
    {
        public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateOnly To { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public string? Note { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var result = await admin.AddTimeOffAsync(ShopId, new TimeOffInput(Input.From, Input.To, Input.Note), ct);
        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadAsync(ct);
            return Page();
        }
        TempData["Saved"] = T["Saved"].Value;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        await admin.DeleteTimeOffAsync(ShopId, id, ct);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var shop = await admin.GetShopAsync(ShopId, ct);
        var zone = ShopTime.FindZone(shop.TimeZoneId);
        Upcoming = (await admin.ListUpcomingTimeOffAsync(ShopId, ct))
            // End is exclusive (midnight after the last day off).
            .Select(x => (x.Id, ShopTime.LocalDate(x.StartUtc, zone), ShopTime.LocalDate(x.EndUtc.AddTicks(-1), zone), x.Note))
            .ToList();
    }
}
