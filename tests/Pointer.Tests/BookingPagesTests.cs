using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Domain;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class BookingPagesTests(PostgresFixture fixture) : IDisposable
{
    private readonly WebAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A shop open every day 00:00-23:45 so tests can book "tomorrow" whatever the real date is.</summary>
    private async Task<(Shop Shop, Service Free, Service Paid)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var slug = $"b-{Guid.NewGuid():N}"[..20];
        var shop = await new ShopProvisioning(db, TimeProvider.System).CreateAsync(slug, "Kapper Test", $"{slug}@example.com", "x");
        await db.OpeningHours.Where(h => h.ShopId == shop.Id).ExecuteDeleteAsync();
        foreach (var d in Enum.GetValues<DayOfWeek>())
            db.OpeningHours.Add(new OpeningHours { ShopId = shop.Id, DayOfWeek = d, Opens = new(0, 0), Closes = new(23, 45) });
        var free = new Service { ShopId = shop.Id, Name = "Baard", DurationMinutes = 15, PriceCents = 1000, DepositCents = 0 };
        var paid = new Service { ShopId = shop.Id, Name = "Knippen", DurationMinutes = 30, PriceCents = 2500, DepositCents = 500 };
        db.Services.AddRange(free, paid);
        await db.SaveChangesAsync();
        return (shop, free, paid);
    }

    private static string TomorrowNoonUtc() => DateTime.UtcNow.Date.AddDays(1).AddHours(11).ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static async Task<string> TokenAsync(HttpClient client, string url) =>
        TokenRegex().Match(await client.GetStringAsync(url)).Groups[1].Value;

    [Fact]
    public async Task Unknown_shop_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/bestaat-niet")).StatusCode);
    }

    [Fact]
    public async Task Booking_page_lists_active_services_and_live_slots()
    {
        var (shop, free, _) = await SeedAsync();
        var client = Client();

        var page = await client.GetStringAsync($"/{shop.Slug}");
        var day = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var slots = await client.GetStringAsync($"/{shop.Slug}/tijden?service={free.Id}&date={day}");

        Assert.Contains("Baard", page);
        Assert.Contains("Knippen", page);
        Assert.Contains("voorschot online", page);
        Assert.Contains("/gegevens?service=", slots);
    }

    [Fact]
    public async Task Details_page_shows_the_rules_before_payment()
    {
        var (shop, _, paid) = await SeedAsync();

        var html = WebUtility.HtmlDecode(await Client().GetStringAsync($"/{shop.Slug}/gegevens?service={paid.Id}&start={Uri.EscapeDataString(TomorrowNoonUtc())}"));

        Assert.Contains("voorschot van € 5,00", html);
        Assert.Contains("houdt de zaak het voorschot", html);
        Assert.Contains("Gratis annuleren kan tot", html);
    }

    [Fact]
    public async Task Booking_without_deposit_confirms_and_can_be_cancelled_by_link()
    {
        var (shop, free, _) = await SeedAsync();
        var client = Client();
        var start = TomorrowNoonUtc();
        var url = $"/{shop.Slug}/gegevens?service={free.Id}&start={Uri.EscapeDataString(start)}";

        var post = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["service"] = free.Id.ToString(), ["start"] = start,
            ["Input.Name"] = "An", ["Input.Email"] = "an@example.com", ["Input.AcceptRules"] = "true",
            ["__RequestVerificationToken"] = await TokenAsync(client, url),
        }));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var confirmation = WebUtility.HtmlDecode(await client.GetStringAsync(post.Headers.Location));
        Assert.Contains("Je afspraak is bevestigd", confirmation);

        await using var db = fixture.CreateContext();
        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.ShopId == shop.Id);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        var cancelUrl = $"/annuleren/{booking.CancelToken}";
        var cancel = await client.PostAsync(cancelUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client, cancelUrl),
        }));
        Assert.Contains("geannuleerd", await cancel.Content.ReadAsStringAsync());
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Booking_requires_accepting_the_rules()
    {
        var (shop, free, _) = await SeedAsync();
        var client = Client();
        var start = TomorrowNoonUtc();
        var url = $"/{shop.Slug}/gegevens?service={free.Id}&start={Uri.EscapeDataString(start)}";

        var post = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["service"] = free.Id.ToString(), ["start"] = start,
            ["Input.Name"] = "An", ["Input.Email"] = "an@example.com",
            ["__RequestVerificationToken"] = await TokenAsync(client, url),
        }));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Bookings.AnyAsync(b => b.ShopId == shop.Id));
    }

    [Fact]
    public async Task Paid_service_without_connected_stripe_account_shows_a_clear_message()
    {
        var (shop, _, paid) = await SeedAsync();
        var client = Client();
        var start = TomorrowNoonUtc();
        var url = $"/{shop.Slug}/gegevens?service={paid.Id}&start={Uri.EscapeDataString(start)}";

        var post = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["service"] = paid.Id.ToString(), ["start"] = start,
            ["Input.Name"] = "An", ["Input.Email"] = "an@example.com", ["Input.AcceptRules"] = "true",
            ["__RequestVerificationToken"] = await TokenAsync(client, url),
        }));

        Assert.Contains("Online betalen lukt nu niet", await post.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancel_link_with_unknown_token_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/annuleren/nope")).StatusCode);
    }

    [Fact]
    public async Task Embed_script_links_to_the_booking_page()
    {
        var response = await Client().GetAsync("/embed.js");
        var js = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("data-shop", js);
        Assert.Contains("Afspraak maken", js);
    }

    [Fact]
    public async Task Pages_send_security_headers()
    {
        var response = await Client().GetAsync("/privacy");

        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task About_page_renders_and_links_to_the_demo()
    {
        var response = await Client().GetAsync("/about");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"functies\"", html);
        Assert.Contains("/img/about/booking.png", html);
    }

    [Theory]
    [InlineData("beheer")]
    [InlineData("about")]
    public async Task Reserved_paths_cannot_become_shop_slugs(string slug)
    {
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ShopProvisioning(db, TimeProvider.System).CreateAsync(slug, "x", "x@example.com", "x"));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
