using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Beheer;

public class InstellingenModel(ShopAdminService admin, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    [BindProperty] public SettingsForm Input { get; set; } = new();

    public sealed class SettingsForm
    {
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public string? OwnerEmail { get; set; }
        public NoShowPolicy NoShowPolicy { get; set; }
        public int FreeCancellationHours { get; set; }
        public int ReminderHoursBefore { get; set; }
        public bool OwnerInstantEmails { get; set; }
        public bool OwnerDailyOverview { get; set; }
        public bool NoShowNoticeEnabled { get; set; }
        public string? PaymentAccountId { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var s = await admin.GetShopAsync(ShopId, ct);
        Input = new SettingsForm
        {
            Name = s.Name,
            Address = s.Address,
            OwnerEmail = s.OwnerEmail,
            NoShowPolicy = s.NoShowPolicy,
            FreeCancellationHours = s.FreeCancellationMinutes / 60,
            ReminderHoursBefore = s.ReminderHoursBefore,
            OwnerInstantEmails = s.OwnerInstantEmails,
            OwnerDailyOverview = s.OwnerDailyOverview,
            NoShowNoticeEnabled = s.NoShowNoticeEnabled,
            PaymentAccountId = s.PaymentAccountId,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await admin.SaveSettingsAsync(ShopId, new SettingsInput(
            Input.Name, Input.Address, Input.OwnerEmail, Input.NoShowPolicy, Input.FreeCancellationHours,
            Input.ReminderHoursBefore, Input.OwnerInstantEmails, Input.OwnerDailyOverview, Input.NoShowNoticeEnabled,
            Input.PaymentAccountId), ct);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return Page();
        }
        TempData["Saved"] = T["Saved"].Value;
        return RedirectToPage();
    }
}
