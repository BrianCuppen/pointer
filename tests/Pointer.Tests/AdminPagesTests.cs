using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Admin;
using Pointer.Core.Domain;
using Pointer.Web.Auth;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class AdminPagesTests(PostgresFixture fixture) : IDisposable
{
    private readonly WebAppFactory _factory = new(fixture);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(Shop Shop, string Email)> ProvisionAsync(string password)
    {
        await using var db = fixture.CreateContext();
        var slug = $"a-{Guid.NewGuid():N}"[..20];
        var email = $"{slug}@example.com";
        var hash = new OwnerAuth(db, new PasswordHasher<ShopUser>()).HashPassword(password);
        return (await new ShopProvisioning(db, TimeProvider.System).CreateAsync(slug, "Zaak " + slug, email, hash), email);
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return TokenRegex().Match(html).Groups[1].Value;
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var token = await AntiforgeryTokenAsync(client, "/beheer/inloggen");
        var response = await client.PostAsync("/beheer/inloggen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Admin_requires_sign_in()
    {
        var response = await Client().GetAsync("/beheer/diensten");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/beheer/inloggen", response.Headers.Location!.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_does_not_sign_in()
    {
        var (_, email) = await ProvisionAsync("the-right-password");
        var client = Client();
        var token = await AntiforgeryTokenAsync(client, "/beheer/inloggen");

        var response = await client.PostAsync("/beheer/inloggen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = "the-wrong-password", ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("klopt niet", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_adds_a_service_and_sees_only_their_own()
    {
        var (mine, email) = await ProvisionAsync("password-one-123");
        var (theirs, _) = await ProvisionAsync("password-two-123");
        await using (var db = fixture.CreateContext())
        {
            db.Services.Add(new Service { ShopId = theirs.Id, Name = "Geheime dienst", DurationMinutes = 30 });
            await db.SaveChangesAsync();
        }
        var client = Client();
        await SignInAsync(client, email, "password-one-123");

        var token = await AntiforgeryTokenAsync(client, "/beheer/diensten/bewerken");
        var post = await client.PostAsync("/beheer/diensten/bewerken", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "Knippen", ["Input.DurationMinutes"] = "30", ["Input.PriceEuro"] = "25",
            ["Input.DepositEuro"] = "5", ["Input.IsActive"] = "true", ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var list = await client.GetStringAsync("/beheer/diensten");
        Assert.Contains("Knippen", list);
        Assert.DoesNotContain("Geheime dienst", list);
        await using var check = fixture.CreateContext();
        Assert.Equal(500, (await check.Services.SingleAsync(s => s.ShopId == mine.Id)).DepositCents);
    }

    [Fact]
    public async Task Editing_another_shops_service_by_id_is_not_found()
    {
        var (_, email) = await ProvisionAsync("password-one-123");
        var (theirs, _) = await ProvisionAsync("password-two-123");
        Guid serviceId;
        await using (var db = fixture.CreateContext())
        {
            var service = new Service { ShopId = theirs.Id, Name = "Andere", DurationMinutes = 30 };
            db.Services.Add(service);
            await db.SaveChangesAsync();
            serviceId = service.Id;
        }
        var client = Client();
        await SignInAsync(client, email, "password-one-123");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/beheer/diensten/bewerken/{serviceId}")).StatusCode);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var (_, email) = await ProvisionAsync("password-one-123");
        var client = Client();
        await SignInAsync(client, email, "password-one-123");

        var post = await client.PostAsync("/beheer/instellingen", new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Name"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
