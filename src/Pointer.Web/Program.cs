using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Pointer.Core.Data;
using Pointer.Core.Domain;
using Pointer.Web;
using Pointer.Web.Auth;
using Pointer.Web.Payments;
using Pointer.Web.Setup;

var builder = WebApplication.CreateBuilder(args);
var isCli = Cli.IsCommand(args);
if (isCli)
    builder.Configuration["Notifications:WorkerEnabled"] = "false";

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Beheer");
    o.Conventions.AllowAnonymousToPage("/Beheer/Inloggen");
}).AddViewLocalization();
builder.Services.AddPointerCore(builder.Configuration);

builder.Services.AddScoped<IPasswordHasher<ShopUser>, PasswordHasher<ShopUser>>();
builder.Services.AddScoped<OwnerAuth>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/beheer/inloggen";
        o.LogoutPath = "/beheer/uitloggen";
        o.Cookie.Name = "pointer_owner";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 10 login attempts per minute per client address.
    o.AddPolicy(OwnerAuth.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PointerDbContext>().Database.MigrateAsync();
}

if (isCli)
    return await Cli.RunAsync(app.Services, args);

if (app.Configuration.GetValue<bool>("Demo:Seed"))
{
    using var scope = app.Services.CreateScope();
    await DemoSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Dutch first. French and English are added by adding resource files and cultures here.
string[] cultures = ["nl-BE", "nl-NL"];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("nl-BE"),
    SupportedCultures = cultures.Select(c => new CultureInfo(c)).ToList(),
    SupportedUICultures = cultures.Select(c => new CultureInfo(c)).ToList(),
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapStripeWebhook();
app.MapRazorPages();

app.Run();
return 0;

public partial class Program;
