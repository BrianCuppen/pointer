using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;

namespace Pointer.Web.Pages.Beheer;

public class OpeningsurenModel(ShopAdminService admin, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    [BindProperty] public List<DayForm> Days { get; set; } = [];

    public sealed class DayForm
    {
        public DayOfWeek Day { get; set; }
        public TimeOnly? Opens { get; set; }
        public TimeOnly? Closes { get; set; }
        public TimeOnly? Opens2 { get; set; }
        public TimeOnly? Closes2 { get; set; }
    }

    public string DayName(DayOfWeek day) => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var week = await admin.GetWeekAsync(ShopId, ct);
        Days = week.Select(d => new DayForm { Day = d.Day, Opens = d.Opens, Closes = d.Closes, Opens2 = d.Opens2, Closes2 = d.Closes2 }).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await admin.SaveWeekAsync(ShopId,
            Days.Select(d => new HoursInput(d.Day, d.Opens, d.Closes, d.Opens2, d.Closes2)).ToList(), ct);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError("", $"{DayName(Enum.Parse<DayOfWeek>(e.Field))}: {T[e.MessageKey]}");
            return Page();
        }

        TempData["Saved"] = T["Saved"].Value;
        return RedirectToPage();
    }
}
