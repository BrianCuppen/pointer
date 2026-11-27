using Microsoft.AspNetCore.Html;

namespace Pointer.Web;

/// <summary>
/// Inline SVG icons (24px grid, 2px rounded strokes, in the style of Lucide). Inline so they
/// inherit color and need no extra request. Decorative by default: hidden from screen readers.
/// </summary>
public static class Icon
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["calendar"] = "<rect x='3' y='4.5' width='18' height='16.5' rx='3'/><path d='M3 9.5h18M8 2.5v4M16 2.5v4'/>",
        ["calendar-x"] = "<rect x='3' y='4.5' width='18' height='16.5' rx='3'/><path d='M3 9.5h18M8 2.5v4M16 2.5v4M10 13.5l4 4M14 13.5l-4 4'/>",
        ["clock"] = "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>",
        ["scissors"] = "<circle cx='6' cy='6' r='3'/><circle cx='6' cy='18' r='3'/><path d='M20 4 8.1 15.9M14.5 14.5 20 20M8.1 8.1 12 12'/>",
        ["map-pin"] = "<path d='M12 21s-7-6.2-7-11.5A7 7 0 0 1 19 9.5C19 14.8 12 21 12 21Z'/><circle cx='12' cy='9.5' r='2.5'/>",
        ["arrow-right"] = "<path d='M5 12h14M13 6l6 6-6 6'/>",
        ["arrow-left"] = "<path d='M19 12H5M11 18l-6-6 6-6'/>",
        ["chevron-left"] = "<path d='m15 18-6-6 6-6'/>",
        ["chevron-right"] = "<path d='m9 18 6-6-6-6'/>",
        ["check"] = "<path d='M5 12.5 10 17.5 19.5 7'/>",
        ["check-circle"] = "<circle cx='12' cy='12' r='9'/><path d='m8.5 12.2 2.4 2.4 4.8-5'/>",
        ["x"] = "<path d='M6 6l12 12M18 6 6 18'/>",
        ["user"] = "<circle cx='12' cy='8' r='4'/><path d='M4 21a8 8 0 0 1 16 0'/>",
        ["users"] = "<circle cx='9' cy='8' r='3.5'/><path d='M2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 7M18 14a6.5 6.5 0 0 1 3.5 6'/>",
        ["mail"] = "<rect x='3' y='5' width='18' height='14' rx='3'/><path d='m3.5 7 8.5 6 8.5-6'/>",
        ["phone"] = "<path d='M5 4h3.5l1.5 4.5-2 1.5a11 11 0 0 0 6 6l1.5-2L20 15.5V19a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2Z'/>",
        ["shield"] = "<path d='M12 3 4.5 6v5.5c0 4.6 3.2 8.4 7.5 9.5 4.3-1.1 7.5-4.9 7.5-9.5V6L12 3Z'/><path d='m9 12 2 2 4-4'/>",
        ["lock"] = "<rect x='4.5' y='10.5' width='15' height='10.5' rx='2.5'/><path d='M8 10.5V7a4 4 0 0 1 8 0v3.5'/>",
        ["settings"] = "<path d='M4 7h10M18 7h2M4 17h4M12 17h8'/><circle cx='16' cy='7' r='2'/><circle cx='10' cy='17' r='2'/>",
        ["list"] = "<path d='M9 6h11M9 12h11M9 18h11'/><circle cx='4.5' cy='6' r='1'/><circle cx='4.5' cy='12' r='1'/><circle cx='4.5' cy='18' r='1'/>",
        ["sun"] = "<circle cx='12' cy='12' r='4'/><path d='M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4'/>",
        ["log-out"] = "<path d='M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 16l-4-4 4-4M6 12h10'/>",
        ["external"] = "<path d='M14 4h6v6M20 4l-9 9M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4'/>",
        ["copy"] = "<rect x='8' y='8' width='12' height='12' rx='2.5'/><path d='M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2'/>",
        ["menu"] = "<path d='M4 7h16M4 12h16M4 17h16'/>",
        ["info"] = "<circle cx='12' cy='12' r='9'/><path d='M12 11v5M12 8h.01'/>",
        ["alert"] = "<path d='M12 3.5 2.5 20h19L12 3.5Z'/><path d='M12 10v4M12 17h.01'/>",
        ["euro"] = "<path d='M17.5 6.5A6.5 6.5 0 1 0 17.5 17.5M4 10.5h9M4 13.5h9'/>",
        ["sparkle"] = "<path d='M12 3v4M12 17v4M3 12h4M17 12h4M6.3 6.3l2.5 2.5M15.2 15.2l2.5 2.5M6.3 17.7l2.5-2.5M15.2 8.8l2.5-2.5'/>",
        ["search"] = "<circle cx='11' cy='11' r='6.5'/><path d='m20 20-4.2-4.2'/>",
        ["download"] = "<path d='M12 4v11M7 10l5 5 5-5M5 20h14'/>",
        ["trash"] = "<path d='M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V4.5h6V7'/>",
        ["plus"] = "<path d='M12 5v14M5 12h14'/>",
        ["edit"] = "<path d='M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16v4Z'/><path d='m13.5 6.5 4 4'/>",
        ["hourglass"] = "<path d='M6 3h12M6 21h12M7 3c0 5 10 5 10 9s-10 4-10 9M17 3c0 5-10 5-10 9s10 4 10 9'/>",
        ["card"] = "<rect x='2.5' y='5' width='19' height='14' rx='3'/><path d='M2.5 10h19M6.5 15h4'/>",
        ["code"] = "<path d='m8 8-4 4 4 4M16 8l4 4-4 4M14 5l-4 14'/>",
        ["database"] = "<ellipse cx='12' cy='6' rx='7.5' ry='3'/><path d='M4.5 6v12c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V6M4.5 12c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3'/>",
        ["zap"] = "<path d='M13 2.5 4.5 13.5H12l-1 8 8.5-11H12l1-8Z'/>",
        ["eye-off"] = "<path d='M3 3l18 18M10.6 5.1A10 10 0 0 1 12 5c5 0 9 4.5 9.8 7a12 12 0 0 1-2.6 3.8M6.6 6.6C4.4 8 2.9 10.1 2.2 12c.8 2.5 4.8 7 9.8 7 1.6 0 3-.4 4.3-1.1M9.9 9.9a3 3 0 0 0 4.2 4.2'/>",
    };

    public static IHtmlContent Svg(string name, string? cssClass = null) =>
        new HtmlString(
            $"<svg class=\"icon{(cssClass is null ? "" : " " + cssClass)}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{Paths[name]}</svg>");

    /// <summary>The Pointer mark: a location pin whose point is a clock hand.</summary>
    public static IHtmlContent Mark(string cssClass = "mark") =>
        new HtmlString(
            $"<svg class=\"{cssClass} brand-mark\" viewBox=\"0 0 32 32\" fill=\"none\" aria-hidden=\"true\" focusable=\"false\">" +
            "<rect x='1' y='1' width='30' height='30' rx='9' fill='currentColor'/>" +
            "<circle cx='16' cy='14' r='7' style='stroke:var(--brand-ink)' stroke-width='2.2'/>" +
            "<path d='M16 10.5V14l2.5 1.6' style='stroke:var(--brand-ink)' stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round'/>" +
            "<path d='M16 21v4.5' style='stroke:var(--brand-ink)' stroke-width='2.2' stroke-linecap='round'/></svg>");
}
