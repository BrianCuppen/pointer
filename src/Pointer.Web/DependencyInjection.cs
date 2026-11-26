using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Data;

namespace Pointer.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddPointerCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set the ConnectionStrings__Default environment variable.");

        services.AddDbContext<PointerDbContext>(o => o.UsePointerDatabase(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.Configure<BookingOptions>(configuration.GetSection(BookingOptions.SectionName));
        services.Configure<AvailabilityOptions>(configuration.GetSection(AvailabilityOptions.SectionName));
        services.AddScoped<AvailabilityService>();
        services.AddScoped<BookingService>();
        return services;
    }
}
