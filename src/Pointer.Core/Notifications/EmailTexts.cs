using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace Pointer.Core.Notifications;

/// <summary>Email strings from EmailTexts.resx with {Name} placeholders.</summary>
public static partial class EmailTexts
{
    private static readonly ResourceManager Resources = new("Pointer.Core.Notifications.EmailTexts", typeof(EmailTexts).Assembly);

    public static string Get(string key, CultureInfo culture, IReadOnlyDictionary<string, string> values)
    {
        var template = Resources.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing email text '{key}'.");
        return Placeholder().Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new InvalidOperationException($"No value for '{{{m.Groups[1].Value}}}' in '{key}'."));
    }

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Placeholder();
}
