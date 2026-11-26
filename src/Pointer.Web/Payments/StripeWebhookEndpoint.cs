using Microsoft.Extensions.Options;
using Pointer.Core.Payments;
using Stripe;

namespace Pointer.Web.Payments;

public static class StripeWebhookEndpoint
{
    public const string Path = "/webhooks/stripe";

    public static IEndpointRouteBuilder MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, HandleAsync).DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<StripeOptions> options,
        PaymentWebhookProcessor processor,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(StripeWebhookEndpoint));
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogError("Stripe webhook received but Stripe__WebhookSecret is not set");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(ct);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, request.Headers["Stripe-Signature"], secret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Rejected Stripe webhook with an invalid signature");
            return Results.BadRequest();
        }

        var paymentEvent = StripeEventMapper.Map(stripeEvent);
        if (paymentEvent is null)
            return Results.Ok();

        // Any exception propagates as a 500, so Stripe retries; processing is idempotent.
        var outcome = await processor.ProcessAsync(paymentEvent, ct);
        logger.LogInformation("Stripe event {EventId} ({Type}): {Outcome}", stripeEvent.Id, stripeEvent.Type, outcome);
        return Results.Ok();
    }
}
