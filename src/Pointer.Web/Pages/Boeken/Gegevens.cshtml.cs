using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Web.Pages.Boeken;

[EnableRateLimiting(BookingRateLimit)]
public class GegevensModel(
    PublicBookingQueries queries,
    AvailabilityService availability,
    BookingService bookings,
    DepositService deposits,
    IStringLocalizer<SharedResource> t) : BookingPageModel(queries)
{
    public const string BookingRateLimit = "booking";

    public Service Service { get; private set; } = null!;
    public DateTime Start { get; private set; }

    [BindProperty] public DetailsForm Input { get; set; } = new();

    public sealed class DetailsForm
    {
        [Required, MaxLength(200)] public string Name { get; set; } = "";
        [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = "";
        [MaxLength(32)] public string? Phone { get; set; }
        public bool AcceptRules { get; set; }
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] Guid service, [FromQuery] DateTimeOffset start, CancellationToken ct) =>
        await LoadAsync(service, start, ct) ?? Page();

    public async Task<IActionResult> OnPostAsync([FromForm] Guid service, [FromForm] DateTimeOffset start, CancellationToken ct)
    {
        if (await LoadAsync(service, start, ct) is { } notFound)
            return notFound;

        if (!Input.AcceptRules)
            ModelState.AddModelError("Input.AcceptRules", t["Book_MustAcceptRules"]);
        if (!ModelState.IsValid)
        {
            // Field errors from data annotations are in English by default; show one clear Dutch message instead.
            ModelState.Clear();
            ModelState.AddModelError("", t["Book_CheckFields"]);
            return Page();
        }

        var staff = await Queries.DefaultStaffAsync(Shop.Id, ct);
        if (staff is null)
            return NotFound();

        var created = await bookings.CreatePendingAsync(new CreateBookingRequest(
            Shop.Id, Service.Id, staff.Id, Start, Input.Name, Input.Email, Input.Phone), ct);

        if (created is not CreateBookingResult.Created { Booking: var booking })
        {
            ModelState.AddModelError("", created is CreateBookingResult.SlotTaken ? t["Book_SlotTaken"] : t["Book_SlotUnavailable"]);
            return Page();
        }

        var confirmUrl = Url.Page("/Boeken/Bevestiging", null, new { slug = Shop.Slug, token = booking.CancelToken }, Request.Scheme)!;
        var backUrl = Url.Page("/Boeken/Index", null, new { slug = Shop.Slug, service = Service.Id }, Request.Scheme)!;
        var checkout = await deposits.StartCheckoutAsync(booking.Id, confirmUrl, backUrl, ct);

        return checkout switch
        {
            StartCheckoutResult.Redirect r => Redirect(r.Url),
            StartCheckoutResult.ConfirmedWithoutDeposit => Redirect(confirmUrl),
            _ => Failed(),
        };

        IActionResult Failed()
        {
            ModelState.AddModelError("", t["Book_PaymentUnavailable"]);
            return Page();
        }
    }

    private async Task<IActionResult?> LoadAsync(Guid serviceId, DateTimeOffset start, CancellationToken ct)
    {
        if (!await LoadShopAsync(ct))
            return NotFound();
        var service = await Queries.ActiveServiceAsync(Shop.Id, serviceId, ct);
        if (service is null)
            return NotFound();
        Service = service;
        Start = start.UtcDateTime;

        var staff = await Queries.DefaultStaffAsync(Shop.Id, ct);
        if (staff is null || !await availability.IsWithinScheduleAsync(Shop, Service, staff.Id, Start, ct))
            return RedirectToPage("/Boeken/Index", new { slug = Shop.Slug, service = Service.Id });
        return null;
    }
}
