namespace Pointer.Web;

public static class SecurityHeaders
{
    /// <summary>
    /// Baseline headers. Referrer-Policy same-origin matters here: booking and cancel URLs carry
    /// secret tokens, which must not leak to other sites through the Referer header.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            h.XFrameOptions = "DENY";
            h.ContentSecurityPolicy = "frame-ancestors 'none'";
            return Task.CompletedTask;
        });
        await next();
    });
}
