using Microsoft.EntityFrameworkCore;

namespace Pointer.Core.Data;

public static class DbContextOptionsExtensions
{
    /// <summary>The one place that configures the database provider, shared by the app, design-time tools and tests.</summary>
    public static TBuilder UsePointerDatabase<TBuilder>(this TBuilder options, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        return options;
    }
}
