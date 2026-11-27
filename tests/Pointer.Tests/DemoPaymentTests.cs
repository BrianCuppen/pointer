using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pointer.Core.Bookings;
using Pointer.Core.Domain;
using Pointer.Core.Payments;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class DemoPaymentTests(PostgresFixture fixture)
{
    /// <summary>A pending booking with a €5 deposit two days from now, and its demo checkout path.</summary>
    private async Task<(Booking Booking, string CheckoutPath)> StartDemoCheckoutAsync(WebAppFactory factory)
    {
        await using var db = fixture.CreateContext();
        var s = await TestData.SeedShopAsync(db);
        s.Shop.PaymentAccountId = "acct_demo";
        await db.SaveChangesAsync();

        using var scope = factory.Services.CreateScope();
        var start = DateTime.UtcNow.Date.AddDays(2).AddHours(10);
        var created = await scope.ServiceProvider.GetRequiredService<BookingService>().CreatePendingAsync(
            new CreateBookingRequest(s.Shop.Id, s.Haircut.Id, s.Owner.Id, start, "Demo Klant", "demo.klant@example.com", null));
        var booking = Assert.IsType<CreateBookingResult.Created>(created).Booking;
        var checkout = await scope.ServiceProvider.GetRequiredService<DepositService>()
            .StartCheckoutAsync(booking.Id, "https://ignored/ok", "https://ignored/back");
        return (booking, Assert.IsType<StartCheckoutResult.Redirect>(checkout).Url);
    }

    private static async Task<HttpResponseMessage> PressAsync(HttpClient client, string path, string handler)
    {
        var html = await client.GetStringAsync(path);
        var token = TokenRegex().Match(html).Groups[1].Value;
        return await client.PostAsync($"{path}?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    [Fact]
    public async Task Paying_on_the_demo_checkout_confirms_the_booking_through_the_webhook_logic()
    {
        using var factory = new WebAppFactory(fixture, ("Payments:Mode", "demo"));
        var (booking, path) = await StartDemoCheckoutAsync(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.StartsWith("/demo-betaling/demo_cs_", path);
        var page = await client.GetStringAsync(path);
        Assert.True(page.Contains("5,00"), page);
        var response = await PressAsync(client, path, "Pay");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/bevestiging/{booking.CancelToken}", response.Headers.Location!.OriginalString);
        await using var db = fixture.CreateContext();
        var stored = await db.Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, stored.Status);
        Assert.Equal(PaymentStatus.Succeeded, stored.Payments.Single().Status);
        Assert.Contains(await db.Notifications.Where(n => n.BookingId == booking.Id).Select(n => n.Type).ToListAsync(),
            t => t == NotificationType.CustomerConfirmation);
    }

    [Fact]
    public async Task Cancelling_the_demo_checkout_releases_the_slot()
    {
        using var factory = new WebAppFactory(fixture, ("Payments:Mode", "demo"));
        var (booking, path) = await StartDemoCheckoutAsync(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Redirect, (await PressAsync(client, path, "Cancel")).StatusCode);

        await using var db = fixture.CreateContext();
        var stored = await db.Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, stored.Status);
        Assert.Equal(PaymentStatus.Failed, stored.Payments.Single().Status);
    }

    [Fact]
    public async Task Demo_checkout_does_not_exist_outside_demo_mode()
    {
        using var factory = new WebAppFactory(fixture);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/demo-betaling/demo_cs_anything")).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
