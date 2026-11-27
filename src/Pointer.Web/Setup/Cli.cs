using Pointer.Core.Admin;
using Pointer.Web.Auth;

namespace Pointer.Web.Setup;

/// <summary>
/// Command line tasks run with the app's configuration, for setting up pilot shops by hand:
/// <c>dotnet Pointer.Web.dll create-shop &lt;slug&gt; "&lt;name&gt;" &lt;owner-email&gt;</c>
/// with the password in POINTER_OWNER_PASSWORD (so it never appears in shell history).
/// </summary>
public static class Cli
{
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "create-shop";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: create-shop <slug> \"<name>\" <owner-email>   (password in POINTER_OWNER_PASSWORD)");
            return 2;
        }
        var password = Environment.GetEnvironmentVariable("POINTER_OWNER_PASSWORD");
        if (string.IsNullOrEmpty(password) || password.Length < 10)
        {
            Console.Error.WriteLine("Set POINTER_OWNER_PASSWORD to a password of at least 10 characters.");
            return 2;
        }

        using var scope = services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<OwnerAuth>();
        var shop = await scope.ServiceProvider.GetRequiredService<ShopProvisioning>()
            .CreateAsync(args[1], args[2], args[3], auth.HashPassword(password));
        Console.WriteLine($"Created shop '{shop.Name}' at /{shop.Slug} with owner {shop.OwnerEmail}.");
        return 0;
    }
}
