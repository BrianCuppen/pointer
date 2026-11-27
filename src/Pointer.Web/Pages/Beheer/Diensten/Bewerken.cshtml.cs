using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;

namespace Pointer.Web.Pages.Beheer.Diensten;

public class BewerkenModel(ShopAdminService admin, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public ServiceForm Input { get; set; } = new();

    public sealed class ServiceForm
    {
        public string Name { get; set; } = "";
        public int DurationMinutes { get; set; } = 30;
        public decimal PriceEuro { get; set; }
        public decimal DepositEuro { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null)
            return Page();

        var service = await admin.GetServiceAsync(ShopId, Id.Value, ct);
        if (service is null)
            return NotFound();

        Input = new ServiceForm
        {
            Name = service.Name,
            DurationMinutes = service.DurationMinutes,
            PriceEuro = service.PriceCents / 100m,
            DepositEuro = service.DepositCents / 100m,
            IsActive = service.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Id is not null && await admin.GetServiceAsync(ShopId, Id.Value, ct) is null)
            return NotFound();

        var result = await admin.SaveServiceAsync(ShopId, Id,
            new ServiceInput(Input.Name, Input.DurationMinutes, Input.PriceEuro, Input.DepositEuro, Input.IsActive), ct);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return Page();
        }

        TempData["Saved"] = T["Saved"].Value;
        return RedirectToPage("Index");
    }
}
