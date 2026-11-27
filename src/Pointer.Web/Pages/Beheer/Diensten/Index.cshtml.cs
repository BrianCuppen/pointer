using Microsoft.Extensions.Localization;
using Pointer.Core.Admin;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Beheer.Diensten;

public class IndexModel(ShopAdminService admin, IStringLocalizer<SharedResource> t) : AdminPageModel(t)
{
    public List<Service> Services { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Services = await admin.ListServicesAsync(ShopId, ct);
}
