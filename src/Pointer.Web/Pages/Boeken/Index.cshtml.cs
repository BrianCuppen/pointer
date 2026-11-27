using Microsoft.AspNetCore.Mvc;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;

namespace Pointer.Web.Pages.Boeken;

public class IndexModel(PublicBookingQueries queries, AvailabilityService availability) : BookingPageModel(queries)
{
    public const int DaysShown = 14;

    public Guid? ServiceId { get; private set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public List<Service> Services { get; private set; } = [];
    public Service? Service { get; private set; }
    public List<DateOnly> Days { get; private set; } = [];
    public SlotsView? SlotsView { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "service")] Guid? service, CancellationToken ct)
    {
        ServiceId = service;
        if (!await LoadShopAsync(ct))
            return NotFound();

        Services = await Queries.ActiveServicesAsync(Shop.Id, ct);
        if (ServiceId is null)
            return Page();

        Service = Services.FirstOrDefault(s => s.Id == ServiceId);
        if (Service is null)
            return NotFound();

        var today = ShopTime.LocalDate(DateTime.UtcNow, Zone);
        Days = Enumerable.Range(0, DaysShown).Select(today.AddDays).ToList();
        if (Date is not null)
            SlotsView = await SlotsView.LoadAsync(Queries, availability, Shop, Service, Date.Value, Zone, ct);
        return Page();
    }
}
