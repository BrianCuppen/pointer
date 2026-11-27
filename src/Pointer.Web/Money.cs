using System.Globalization;

namespace Pointer.Web;

public static class Money
{
    private static readonly CultureInfo Euro = CultureInfo.GetCultureInfo("nl-BE");

    public static string Format(long cents) => (cents / 100m).ToString("C", Euro);
}
