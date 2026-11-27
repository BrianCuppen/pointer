using Pointer.Core.Availability;
using Pointer.Core.Bookings;
using Pointer.Core.Data;
using Pointer.Core.Payments;
using Pointer.Web.Payments;

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

        services.Configure<PlatformFeeOptions>(configuration.GetSection(PlatformFeeOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<DepositService>();
        services.AddScoped<PaymentWebhookProcessor>();
        return services;
    }
}
