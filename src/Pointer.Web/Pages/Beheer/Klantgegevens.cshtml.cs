using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;

namespace Pointer.Web.Pages.Beheer;

/// <summary>GDPR requests: look up, export or erase everything stored about one customer email.</summary>
public class KlantgegevensModel(CustomerDataService customers, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    [BindProperty(SupportsGet = true)] public string? Email { get; set; }

    public IReadOnlyList<CustomerBooking>? Results { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(Email))
            Results = await customers.FindAsync(ShopId, Email, ct);
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken ct)
    {
        var json = Email is null ? null : await customers.ExportJsonAsync(ShopId, Email, ct);
        if (json is null)
            return NotFound();
        return File(Encoding.UTF8.GetBytes(json), "application/json", "klantgegevens.json");
    }

    public async Task<IActionResult> OnPostEraseAsync(CancellationToken ct)
    {
        var result = await customers.EraseAsync(ShopId, Email ?? "", ct);
        TempData["Saved"] = T[$"CustomerData_{result}"].Value;
        return result == CustomerDeleteResult.HasUpcomingBookings
            ? RedirectToPage(new { email = Email })
            : RedirectToPage();
    }
}
