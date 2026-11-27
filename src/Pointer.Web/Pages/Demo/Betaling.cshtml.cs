using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Core.Payments;
using Pointer.Web.Payments;

namespace Pointer.Web.Pages.Demo;

/// <summary>
/// The fake checkout of demo payment mode. Its buttons send the same events Stripe would, through
/// the real webhook processor, so the booking, slot and email logic run exactly as with Stripe.
/// </summary>
public class BetalingModel(PointerDbContext db, PaymentWebhookProcessor webhooks, IConfiguration config) : PageModel
{
    public Payment Payment { get; private set; } = null!;
    public Shop Shop { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string session, CancellationToken ct) =>
        await LoadAsync(session, ct) ?? Page();

    public async Task<IActionResult> OnPostPayAsync(string session, CancellationToken ct)
    {
        if (await LoadAsync(session, ct) is { } notFound)
            return notFound;
        await webhooks.ProcessAsync(Event(session, "checkout.session.completed", PaymentEventKind.CheckoutPaid), ct);
        return RedirectToPage("/Boeken/Bevestiging", new { slug = Shop.Slug, token = Payment.Booking!.CancelToken });
    }

    public async Task<IActionResult> OnPostFailAsync(string session, CancellationToken ct)
    {
        if (await LoadAsync(session, ct) is { } notFound)
            return notFound;
        await webhooks.ProcessAsync(Event(session, "checkout.session.async_payment_failed", PaymentEventKind.CheckoutFailed), ct);
        return RedirectToPage("/Boeken/Bevestiging", new { slug = Shop.Slug, token = Payment.Booking!.CancelToken });
    }

    public async Task<IActionResult> OnPostCancelAsync(string session, CancellationToken ct)
    {
        if (await LoadAsync(session, ct) is { } notFound)
            return notFound;
        // Stripe would let the session expire; in the demo, release the slot right away.
        await webhooks.ProcessAsync(Event(session, "checkout.session.expired", PaymentEventKind.CheckoutFailed), ct);
        return RedirectToPage("/Boeken/Index", new { slug = Shop.Slug, service = Payment.Booking!.ServiceId });
    }

    private PaymentEvent Event(string session, string type, PaymentEventKind kind) =>
        new(DemoPaymentGateway.ProviderName, $"demo_evt_{type}_{session}", type, kind, session, $"demo_pi_{session}", Shop.PaymentAccountId);

    private async Task<IActionResult?> LoadAsync(string session, CancellationToken ct)
    {
        if (!DependencyInjection.IsDemoPayments(config))
            return NotFound();
        var payment = await db.Payments.AsNoTracking().Include(p => p.Booking).ThenInclude(b => b!.Service)
            .FirstOrDefaultAsync(p => p.Provider == DemoPaymentGateway.ProviderName && p.ProviderPaymentId == session, ct);
        if (payment is null)
            return NotFound();
        Payment = payment;
        Shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == payment.Booking!.ShopId, ct);
        return null;
    }
}
