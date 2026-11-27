using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Pointer.Web.Embed;

/// <summary>
/// The one-line embed for a shop's own website:
/// <c>&lt;script src="https://host/embed.js" data-shop="slug" async&gt;&lt;/script&gt;</c>
/// It adds a plain link styled by the shop's own site. It deliberately does not open the booking
/// page in an iframe: Stripe Checkout cannot run framed, and browsers block the cookies a framed
/// form needs, so the customer goes to the booking page itself.
/// </summary>
public static class EmbedEndpoint
{
    public static IEndpointRouteBuilder MapEmbed(this IEndpointRouteBuilder app)
    {
        app.MapGet("/embed.js", (HttpContext http, IStringLocalizer<SharedResource> t) =>
        {
            var label = JsonSerializer.Serialize(t["Embed_Button"].Value);
            var js = $$"""
                (function () {
                  var s = document.currentScript;
                  if (!s) return;
                  var slug = s.getAttribute("data-shop");
                  if (!slug) return;
                  var a = document.createElement("a");
                  a.href = new URL(s.src).origin + "/" + encodeURIComponent(slug);
                  a.textContent = s.getAttribute("data-label") || {{label}};
                  a.className = "pointer-book-button";
                  s.parentNode.insertBefore(a, s.nextSibling);
                })();
                """;
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(js, "text/javascript; charset=utf-8");
        });
        return app;
    }
}
