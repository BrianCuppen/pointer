using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pointer.Core.Data;

/// <summary>
/// Stores enums as readable snake_case text (<c>PendingPayment</c> → <c>pending_payment</c>),
/// so raw SQL, constraints and database dumps stay readable and reordering enum members is safe.
/// </summary>
public sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => ToSnakeCase(v.ToString()),
    v => FromSnakeCase(v))
    where TEnum : struct, Enum
{
    public static TEnum FromSnakeCase(string value) => Enum.Parse<TEnum>(value.Replace("_", ""), true);

    public static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
